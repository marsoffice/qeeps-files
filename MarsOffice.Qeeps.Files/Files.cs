using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Microfunction;
using MarsOffice.Qeeps.Files.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;

namespace MarsOffice.Qeeps.Files
{
    public class Files
    {
        private readonly IConfiguration _config;

        public Files(IConfiguration config)
        {
            _config = config;
        }

        [FunctionName("UploadFromService")]
        public async Task<IActionResult> UploadFromService(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/files/uploadFromService")] HttpRequest req,
            ILogger log,
            ClaimsPrincipal principal
            )
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") ?? "Development";
                if (env != "Development" && principal.FindFirstValue("roles") != "Application")
                {
                    return new StatusCodeResult(401);
                }

                var path = req.Query["path"].ToString();

                await req.ReadFormAsync();
                var dtos = new List<FileDto>();

                var connectionStrings = ReadConnectionStrings();
                foreach (var file in req.Form.Files)
                {
                    try
                    {
                        for (var i = 0; i < connectionStrings.Count(); i++)
                        {
                            try
                            {
                                var cs = connectionStrings.ElementAt(i);
                                var cloudStorageAccount = CloudStorageAccount.Parse(cs.ConnectionString);
                                var blobClient = cloudStorageAccount.CreateCloudBlobClient();
                                var blobContainerReference = blobClient.GetContainerReference(cs.Location);
#if DEBUG
                                await blobContainerReference.CreateIfNotExistsAsync();
#endif
                                var fileId = Guid.NewGuid().ToString();
                                var fileRef = blobContainerReference.GetBlockBlobReference(path);
                                using var readStream = file.OpenReadStream();
                                await fileRef.UploadFromStreamAsync(readStream);
                                fileRef.Metadata.Add("filename", WebUtility.UrlEncode(file.FileName));
                                fileRef.Metadata.Add("sizeinbytes", file.Length.ToString());
                                fileRef.Metadata.Add("location", cs.Location);
                                await fileRef.SetMetadataAsync();
                                dtos.Add(new FileDto
                                {
                                    Filename = file.FileName,
                                    SizeInBytes = file.Length,
                                    UserId = null,
                                    UploadSessionId = null,
                                    FileId = fileId,
                                    Location = cs.Location,
                                    Path = path
                                });
                                break;
                            }
                            catch (Exception)
                            {
                                if (i < connectionStrings.Count() - 1)
                                {
                                    continue;
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"File upload failed: {file.FileName}");
                    }
                }
                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("Upload")]
        public async Task<IActionResult> Upload(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/files/upload")] HttpRequest req,
            ILogger log
            )
        {
            try
            {
                var principal = MarsOfficePrincipal.Parse(req);
                string uid;
                if (principal.FindFirstValue("roles") != "Application")
                {
                    uid = principal.FindFirstValue("id");
                }
                else
                {
                    uid = principal.FindFirstValue(ClaimTypes.Name);
                }

                await req.ReadFormAsync();
                var guid = Guid.NewGuid().ToString();
                var dtos = new List<FileDto>();

                var connectionStrings = ReadConnectionStrings();
                foreach (var file in req.Form.Files)
                {
                    try
                    {
                        for (var i = 0; i < connectionStrings.Count(); i++)
                        {
                            try
                            {
                                var cs = connectionStrings.ElementAt(i);
                                var cloudStorageAccount = CloudStorageAccount.Parse(cs.ConnectionString);
                                var blobClient = cloudStorageAccount.CreateCloudBlobClient();
                                var blobContainerReference = blobClient.GetContainerReference(cs.Location);
#if DEBUG
                                await blobContainerReference.CreateIfNotExistsAsync();
#endif
                                var fileId = Guid.NewGuid().ToString();
                                var fileRef = blobContainerReference.GetBlockBlobReference($"{uid}/{guid}_{fileId}");
                                using var readStream = file.OpenReadStream();
                                await fileRef.UploadFromStreamAsync(readStream);
                                fileRef.Metadata.Add("filename", WebUtility.UrlEncode(file.FileName));
                                fileRef.Metadata.Add("sizeinbytes", file.Length.ToString());
                                fileRef.Metadata.Add("location", cs.Location);
                                await fileRef.SetMetadataAsync();
                                dtos.Add(new FileDto
                                {
                                    Filename = file.FileName,
                                    SizeInBytes = file.Length,
                                    UserId = uid,
                                    UploadSessionId = guid,
                                    FileId = fileId,
                                    Location = cs.Location,
                                    Path = $"{uid}/{guid}_{fileId}"
                                });
                                break;
                            }
                            catch (Exception)
                            {
                                if (i < connectionStrings.Count() - 1)
                                {
                                    continue;
                                }
                                else
                                {
                                    throw;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        log.LogError(ex, $"File upload failed: {file.FileName}; uid: {uid}");
                    }
                }
                return new OkObjectResult(dtos);
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        [FunctionName("Download")]
        public async Task<IActionResult> Download(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/files/download/{location}/{uid}/{uploadSessionId}/{fileId}")] HttpRequest req,
            ILogger log
            )
        {
            try
            {
                var uid = req.RouteValues["uid"].ToString();
                var fileId = req.RouteValues["fileId"].ToString();
                var location = req.RouteValues["location"].ToString();
                var uploadSessionId = req.RouteValues["uploadSessionId"].ToString();
                var connectionStrings = ReadConnectionStrings();
                for (var i = 0; i < connectionStrings.Count(); i++)
                {
                    try
                    {
                        var cs = connectionStrings.ElementAt(i);
                        var cloudStorageAccount = CloudStorageAccount.Parse(cs.ConnectionString);
                        var blobClient = cloudStorageAccount.CreateCloudBlobClient();
                        var blobContainerReference = blobClient.GetContainerReference(cs.Location);
#if DEBUG
                        await blobContainerReference.CreateIfNotExistsAsync();
#endif
                        var blobReference = blobContainerReference.GetBlobReference($"{uid}/{uploadSessionId}_{fileId}");
                        if (!await blobReference.ExistsAsync())
                        {
                            if (i < connectionStrings.Count() - 1)
                            {
                                continue;
                            }
                            else
                            {
                                return new NotFoundResult();
                            }
                        }
                        var fileName = blobReference.Metadata["filename"];
                        if (string.IsNullOrEmpty(fileName))
                        {
                            fileName = "download";
                        }
                        req.HttpContext.Response.StatusCode = (int)HttpStatusCode.OK;
                        req.HttpContext.Response.Headers.TryAdd("Content-Disposition", $"attachment; filename=\"{fileName}\"");
                        req.HttpContext.Response.Headers.TryAdd("Content-Type", $"application/octet-stream");
                        using var readStream = await blobReference.OpenReadAsync();
                        await readStream.CopyToAsync(req.HttpContext.Response.Body);
                        await req.HttpContext.Response.Body.FlushAsync();
                        return new EmptyResult();
                    }
                    catch (Exception)
                    {
                        if (i < connectionStrings.Count() - 1)
                        {
                            continue;
                        }
                        else
                        {
                            throw;
                        }
                    }
                }
                return new NotFoundResult();
            }
            catch (Exception e)
            {
                log.LogError(e, "Exception occured in function");
                return new BadRequestObjectResult(Errors.Extract(e));
            }
        }

        private IEnumerable<ConnectionStringInfo> ReadConnectionStrings()
        {
            var result = new List<ConnectionStringInfo>
            {
                new ConnectionStringInfo
                {
                    IsMain = true,
                    Location = _config["location"].Replace(" ", "").ToLower(),
                    ConnectionString = _config["localsaconnectionstring"]
                }
            };
            var othersString = _config["othersaconnectionstrings"];
            if (string.IsNullOrEmpty(othersString))
            {
                return result;
            }
            var splitByComma = othersString.Split(",");
            foreach (var splitByCommaElement in splitByComma)
            {
                var splitByArrow = splitByCommaElement.Split("->");
                if (splitByArrow.Length != 2)
                {
                    continue;
                }
                result.Add(new ConnectionStringInfo
                {
                    ConnectionString = splitByArrow[1],
                    IsMain = false,
                    Location = splitByArrow[0]
                });
            }
            return result;
        }
    }
}
