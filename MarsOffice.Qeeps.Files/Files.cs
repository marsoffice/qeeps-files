using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using MarsOffice.Qeeps.Files.Abstractions;
using MarsOffice.Qeeps.Microfunction;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace MarsOffice.Qeeps.Files
{
    public class Files
    {
        private readonly CloudStorageAccount _cloudStorageAccount;

        public Files(CloudStorageAccount cloudStorageAccount)
        {
            _cloudStorageAccount = cloudStorageAccount;
        }

        [FunctionName("Upload")]
        public async Task<IActionResult> Upload(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/files/upload")] HttpRequest req,
            ILogger log
            )
        {
            var blobClient = _cloudStorageAccount.CreateCloudBlobClient();
            var blobContainerReference = blobClient.GetContainerReference("userfiles");

#if DEBUG
            blobContainerReference.SetPermissions(new BlobContainerPermissions { PublicAccess = BlobContainerPublicAccessType.Blob });
            await blobContainerReference.CreateIfNotExistsAsync();
#endif
            var principal = QeepsPrincipal.Parse(req);
            var uid = principal.FindFirstValue("id");

            await req.ReadFormAsync();
            var guid = Guid.NewGuid().ToString();
            var dtos = new List<FileDto>();

            foreach (var file in req.Form.Files)
            {
                try
                {
                    var fileRef = blobContainerReference.GetBlockBlobReference($"{uid}/{guid}/{file.FileName}");
                    using var readStream = file.OpenReadStream();
                    await fileRef.UploadFromStreamAsync(readStream);
                    dtos.Add(new FileDto {
                        Filename = file.FileName,
                        SizeInBytes = file.Length,
                        UserId = uid,
                        Url = fileRef.Uri.AbsoluteUri.ToString()
                    });
                }
                catch (Exception ex)
                {
                    log.LogError(ex, $"File upload failed: {file.FileName}; uid: {uid}");
                }
            }
            return new OkObjectResult(dtos);
        }
    }
}
