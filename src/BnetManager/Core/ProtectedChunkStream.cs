using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BnetManager.Core;

internal sealed class ProtectedChunkStream(Stream source, bool writing, byte[] key) : Stream
{
    private const int ChunkSize = 1024 * 1024;
    private readonly byte[] buffer = new byte[ChunkSize];
    private readonly byte[] ciphertext = new byte[ChunkSize];
    private readonly AesGcm cipher = new(key, 16);
    private int position;
    private int count;
    private int sequence;
    private bool completed;

    public override bool CanRead => !writing;
    public override bool CanWrite => writing;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => source.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] data, int offset, int length) => Write(data.AsSpan(offset, length));

    public override void Write(ReadOnlySpan<byte> data)
    {
        if (!writing || completed) throw new InvalidOperationException("快照写入流已关闭。");
        while (!data.IsEmpty)
        {
            var length = Math.Min(data.Length, buffer.Length - count);
            data[..length].CopyTo(buffer.AsSpan(count));
            count += length;
            data = data[length..];
            if (count == buffer.Length) WriteChunk();
        }
    }

    private void WriteChunk()
    {
        Span<byte> nonce = stackalloc byte[12];
        nonce.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(nonce[8..], sequence++);
        Span<byte> length = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, count);
        Span<byte> tag = stackalloc byte[16];
        try
        {
            cipher.Encrypt(nonce, buffer.AsSpan(0, count), ciphertext.AsSpan(0, count), tag, length);
            source.Write(length);
            source.Write(ciphertext.AsSpan(0, count));
            source.Write(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer.AsSpan(0, count));
            count = 0;
        }
    }

    public override int Read(byte[] data, int offset, int length) => Read(data.AsSpan(offset, length));

    public override int Read(Span<byte> data)
    {
        if (writing) throw new NotSupportedException();
        if (data.IsEmpty) return 0;
        if (position == count && !ReadChunk()) return 0;
        var length = Math.Min(data.Length, count - position);
        buffer.AsSpan(position, length).CopyTo(data);
        CryptographicOperations.ZeroMemory(buffer.AsSpan(position, length));
        position += length;
        return length;
    }

    private bool ReadChunk()
    {
        if (completed) return false;
        Span<byte> prefix = stackalloc byte[sizeof(int)];
        source.ReadExactly(prefix);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (length < 0 || length > ChunkSize) throw new InvalidDataException("登录快照的加密数据块无效。");
        source.ReadExactly(ciphertext.AsSpan(0, length));
        Span<byte> tag = stackalloc byte[16];
        source.ReadExactly(tag);
        Span<byte> nonce = stackalloc byte[12];
        nonce.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(nonce[8..], sequence++);
        try { cipher.Decrypt(nonce, ciphertext.AsSpan(0, length), tag, buffer.AsSpan(0, length), prefix); }
        catch (CryptographicException error)
        {
            throw new InvalidDataException("登录快照的数据校验失败，请重新登录保存。", error);
        }
        count = length;
        position = 0;
        if (count != 0) return true;
        completed = true;
        if (source.ReadByte() != -1) throw new InvalidDataException("登录快照末尾含有多余数据。");
        return false;
    }

    public void EnsureComplete()
    {
        if (position != count || ReadChunk()) throw new InvalidDataException("登录快照含有未读取的数据。");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (writing && !completed)
                {
                    if (count > 0) WriteChunk();
                    WriteChunk();
                    completed = true;
                }
            }
            finally { CryptographicOperations.ZeroMemory(buffer); cipher.Dispose(); }
        }
        base.Dispose(disposing);
    }
}
