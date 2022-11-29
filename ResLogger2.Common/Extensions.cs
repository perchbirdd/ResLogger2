using System.Text;

namespace ResLogger2.Common;

public static class Extensions
{
	public static string ReadString(this BinaryReader br)
	{
		var chars = new List<byte>();

		byte current;
		while ((current = br.ReadByte()) != 0)
			chars.Add(current);

		return Encoding.ASCII.GetString(chars.ToArray(), 0, chars.Count);
	}
	
	public static unsafe string ToLowerUnsafe(this string asciiString)
	{
		fixed (char* pstr = asciiString)
		{
			for(char* p = pstr; *p != 0; ++p)
				*p = (*p > 0x40) && (*p < 0x5b) ? (char)(*p | 0x60) : (*p);
		}
		return asciiString;
	}
}