using System;
using System.IO;
using CefSharp;

namespace NoteFlow.Studio.CefSharpHandlers
{
    public class JsonResponseFilter : IResponseFilter
    {
        private MemoryStream memoryStream = new MemoryStream();
        private byte[] _finalData;

        public byte[] Data => _finalData ?? memoryStream?.ToArray() ?? Array.Empty<byte>();

        public FilterStatus Filter(Stream dataIn, out long dataInRead, Stream dataOut, out long dataOutWritten)
        {
            if (dataIn == null)
            {
                dataInRead = 0;
                dataOutWritten = 0;
                return FilterStatus.Done;
            }

            dataInRead = dataIn.Length;
            dataOutWritten = Math.Min(dataInRead, dataOut.Length);

            dataIn.CopyTo(dataOut);
            
            dataIn.Position = 0;
            dataIn.CopyTo(memoryStream);

            return FilterStatus.NeedMoreData;
        }

        public bool InitFilter()
        {
            return true;
        }

        public void Dispose()
        {
            if (memoryStream != null)
            {
                _finalData = memoryStream.ToArray();
                memoryStream.Dispose();
                memoryStream = null;
            }
        }
    }
}
