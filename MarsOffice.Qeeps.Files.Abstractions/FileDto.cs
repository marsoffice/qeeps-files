using System;
using System.Collections.Generic;

namespace MarsOffice.Qeeps.Files.Abstractions
{
    public class FileDto
    {
        public string UserId { get; set; }
        public string Filename { get; set; }
        public long SizeInBytes { get; set; }
        public string Url { get; set; }
    }
}
