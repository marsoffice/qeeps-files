using System;
using System.Collections.Generic;

namespace MarsOffice.Qeeps.Files.Abstractions
{
    public class FileDto
    {
        public string FileId { get; set; }
        public string Location { get; set; }
        public string UserId { get; set; }
        public string UploadSessionId { get; set; }
        public string Filename { get; set; }
        public long SizeInBytes { get; set; }
    }
}
