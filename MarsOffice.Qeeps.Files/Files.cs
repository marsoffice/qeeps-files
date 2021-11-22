using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Files.Abstractions;
using MarsOffice.Qeeps.Microfunction;
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

        [FunctionName("Upload")]
        public async Task<IActionResult> Upload(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/files/upload")] HttpRequest req,
            ILogger log
            )
        {
            try
            {
                var principal = QeepsPrincipal.Parse(req);
                var uid = principal.FindFirstValue("id");
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
                                var blobContainerReference = blobClient.GetContainerReference("userfiles");
#if DEBUG
                                await blobContainerReference.CreateIfNotExistsAsync();
#endif
                                var fileId = Guid.NewGuid().ToString();
                                var fileRef = blobContainerReference.GetBlockBlobReference($"{cs.Location}/{uid}/{guid}_{fileId}");
                                using var readStream = file.OpenReadStream();
                                await fileRef.UploadFromStreamAsync(readStream);
                                fileRef.Metadata.Add("filename", WebUtility.UrlEncode(file.FileName));
                                fileRef.Metadata.Add("sizeinbytes", file.Length.ToString());
                                await fileRef.SetMetadataAsync();
                                dtos.Add(new FileDto
                                {
                                    Filename = file.FileName,
                                    SizeInBytes = file.Length,
                                    UserId = uid,
                                    UploadSessionId = guid,
                                    FileId = fileId,
                                    Location = cs.Location
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
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/files/download/{location}/{uid}/{fileId}")] HttpRequest req,
            ILogger log
            )
        {
            try
            {
                var connectionStrings = ReadConnectionStrings();
                for (var i = 0; i < connectionStrings.Count(); i++)
                {
                    try
                    {
                        var cs = connectionStrings.ElementAt(i);
                        var cloudStorageAccount = CloudStorageAccount.Parse(cs.ConnectionString);
                        var blobClient = cloudStorageAccount.CreateCloudBlobClient();
                        var blobContainerReference = blobClient.GetContainerReference("userfiles");
#if DEBUG
                        await blobContainerReference.CreateIfNotExistsAsync();
#endif
                        var uid = req.RouteValues["uid"].ToString();
                        var fileId = req.RouteValues["fileId"].ToString();
                        var location = req.RouteValues["location"].ToString();
                        var blobReference = blobContainerReference.GetBlobReference($"{location}/{uid}/{fileId}");
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
