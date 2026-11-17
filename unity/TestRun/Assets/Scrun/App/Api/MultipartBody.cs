using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Scrun.App
{
    // multipart/form-data 본문(계획 0038 계기판 사진). UnityWebRequest의 WWWForm·MultipartFormFileSection 대신 직접 만듦:
    // 같은 칸 이름(photos)으로 파일 여러 개를 붙이고, 가짜 HTTP 테스트에서 본문을 그대로 볼 수 있게
    public sealed class MultipartBody
    {
        public readonly byte[] Bytes;
        public readonly string ContentType;

        MultipartBody(byte[] bytes, string boundary)
        {
            Bytes = bytes;
            ContentType = "multipart/form-data; boundary=" + boundary;
        }

        public readonly struct FilePart
        {
            public readonly string FileName;
            public readonly byte[] Data;
            public readonly string MimeType;

            public FilePart(string fileName, byte[] data, string mimeType = "image/jpeg")
            {
                FileName = fileName;
                Data = data;
                MimeType = mimeType;
            }
        }

        public static MultipartBody Files(string field, IReadOnlyList<FilePart> files, string boundary = null)
        {
            boundary ??= "scrun-" + Guid.NewGuid().ToString("N");
            using var ms = new MemoryStream();
            void Write(string s)
            {
                var b = Encoding.UTF8.GetBytes(s);
                ms.Write(b, 0, b.Length);
            }
            foreach (var f in files)
            {
                Write($"--{boundary}\r\n");
                Write($"Content-Disposition: form-data; name=\"{field}\"; filename=\"{Safe(f.FileName)}\"\r\n");
                Write($"Content-Type: {f.MimeType}\r\n\r\n");
                ms.Write(f.Data, 0, f.Data.Length);
                Write("\r\n");
            }
            Write($"--{boundary}--\r\n");
            return new MultipartBody(ms.ToArray(), boundary);
        }

        // 파일 이름에 따옴표·줄바꿈이 들어가면 머리가 깨짐
        static string Safe(string name) => (name ?? "photo.jpg").Replace("\"", "").Replace("\r", "").Replace("\n", "");
    }
}
