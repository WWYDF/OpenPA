using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using static mumblelib.Text;

namespace mumblelib
{
    public unsafe class MumbleLinkFile : IDisposable
    {

        /// <summary>Holds a reference to the shared memory block.</summary>
        private readonly MemoryMappedFile memoryMappedFile;

        private readonly Frame* ptr;

        /// <summary>Indicates whether this object is disposed.</summary>
        private bool disposed;

        private static string LinkFileName()
        {
            return "MumbleLink";
        }

        public MumbleLinkFile(MemoryMappedFile memoryMappedFile)
        {
            this.memoryMappedFile = memoryMappedFile ?? throw new ArgumentNullException("memoryMappedFile");
            byte* tmp = null;
            memoryMappedFile.CreateViewAccessor().SafeMemoryMappedViewHandle.AcquirePointer(ref tmp);
            ptr = (Frame*)tmp;
        }

        public Frame* FramePtr()
        {
            return ptr;
        }

        public static MumbleLinkFile CreateOrOpen()
        {
            return new MumbleLinkFile(MemoryMappedFile.CreateOrOpen(LinkFileName(), Marshal.SizeOf<Frame>()));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void assertNotDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }
            if (disposing)
            {
                memoryMappedFile.Dispose();
            }
            disposed = true;
        }
    }
}
