using System.Runtime.CompilerServices;
using System.Text;

namespace Daisi.Minion.Engine;

/// <summary>
/// Wraps a token stream to fix broken multi-byte UTF-8 sequences.
///
/// When a tokenizer decodes byte fallback tokens (like emoji) one at a time,
/// each byte becomes an isolated char — e.g. 😊 (F0 9F 98 8A) becomes four
/// replacement characters (�). This filter detects lone high bytes (0x80+)
/// and buffers them until a complete UTF-8 sequence can be reconstructed,
/// then emits the proper character.
///
/// Also strips fullwidth pipes (U+FF5C ｜) → ASCII pipes (|) for Qwen models.
/// </summary>
public static class TokenStreamFixer
{
    public static async IAsyncEnumerable<string> Fix(
        IAsyncEnumerable<string> tokens,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var byteBuffer = new List<byte>();
        int expectedBytes = 0;

        await foreach (var token in tokens.WithCancellation(ct))
        {
            // Normalize fullwidth pipes (Qwen special tokens)
            var text = token.Contains('\uFF5C')
                ? token.Replace('\uFF5C', '|')
                : token;

            // Check for broken byte fallback characters
            // The tokenizer decodes <0xNN> as (char)b, producing chars in 0x80-0xFF
            // that are actually individual UTF-8 bytes needing reassembly.
            var result = new StringBuilder();
            foreach (var ch in text)
            {
                if (ch >= 0x80 && ch <= 0xFF)
                {
                    // This looks like a raw byte from a byte fallback token
                    var b = (byte)ch;

                    if (byteBuffer.Count == 0)
                    {
                        // Starting a new multi-byte sequence — determine expected length
                        if ((b & 0xE0) == 0xC0) expectedBytes = 2;      // 110xxxxx → 2 bytes
                        else if ((b & 0xF0) == 0xE0) expectedBytes = 3; // 1110xxxx → 3 bytes
                        else if ((b & 0xF8) == 0xF0) expectedBytes = 4; // 11110xxx → 4 bytes
                        else
                        {
                            // Lone continuation byte or invalid — pass through
                            result.Append(ch);
                            continue;
                        }
                        byteBuffer.Add(b);
                    }
                    else
                    {
                        // Continuation byte (10xxxxxx)
                        byteBuffer.Add(b);
                    }

                    // Check if we have a complete sequence
                    if (byteBuffer.Count >= expectedBytes)
                    {
                        result.Append(Encoding.UTF8.GetString(byteBuffer.ToArray()));
                        byteBuffer.Clear();
                        expectedBytes = 0;
                    }
                }
                else
                {
                    // Regular ASCII/BMP char — flush any pending bytes first
                    if (byteBuffer.Count > 0)
                    {
                        result.Append(Encoding.UTF8.GetString(byteBuffer.ToArray()));
                        byteBuffer.Clear();
                        expectedBytes = 0;
                    }
                    result.Append(ch);
                }
            }

            if (result.Length > 0)
                yield return result.ToString();
        }

        // Flush any remaining bytes at end of stream
        if (byteBuffer.Count > 0)
            yield return Encoding.UTF8.GetString(byteBuffer.ToArray());
    }
}
