using System.Text;

namespace Condo.Api.Services;

/// <summary>
/// Lee el versionCode del AndroidManifest.xml binario (AXML) que viene dentro de un APK. Solo recorre lo necesario: la tabla de cadenas, el
/// mapa de recursos y los atributos del elemento <c>manifest</c>. El versionCode es el atributo con el recurso 0x0101021b.
/// </summary>
public static class AndroidManifestReader
{
    private const ushort ChunkXml = 0x0003;
    private const ushort ChunkStringPool = 0x0001;
    private const ushort ChunkResourceMap = 0x0180;
    private const ushort ChunkStartElement = 0x0102;
    private const uint VersionCodeResourceId = 0x0101021b;
    private const byte TypeIntDec = 0x10;
    private const byte TypeIntHex = 0x11;

    /// <summary>El versionCode del manifest, o null si el contenido no es un AXML valido o no trae ese atributo.</summary>
    public static int? ReadVersionCode(byte[] axml)
    {
        try
        {
            if (axml.Length < 8 || BitConverter.ToUInt16(axml, 0) != ChunkXml) return null;

            List<string>? strings = null;
            uint[] resourceIds = [];
            int offset = BitConverter.ToUInt16(axml, 2); // tamaño del encabezado del archivo

            while (offset + 8 <= axml.Length)
            {
                var type = BitConverter.ToUInt16(axml, offset);
                var headerSize = BitConverter.ToUInt16(axml, offset + 2);
                var size = (int)BitConverter.ToUInt32(axml, offset + 4);
                if (size < 8 || offset + size > axml.Length) return null;

                switch (type)
                {
                    case ChunkStringPool:
                        strings = ReadStringPool(axml, offset);
                        break;
                    case ChunkResourceMap:
                        resourceIds = new uint[(size - headerSize) / 4];
                        for (var i = 0; i < resourceIds.Length; i++)
                            resourceIds[i] = BitConverter.ToUInt32(axml, offset + headerSize + i * 4);
                        break;
                    case ChunkStartElement when strings is not null:
                        var code = ReadManifestVersionCode(axml, offset, headerSize, strings, resourceIds);
                        if (code.HasValue) return code;
                        break;
                }

                offset += size;
            }

            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static int? ReadManifestVersionCode(byte[] data, int chunk, int headerSize, List<string> strings, uint[] resourceIds)
    {
        var body = chunk + headerSize;
        var nameIndex = BitConverter.ToInt32(data, body + 4);
        if (nameIndex < 0 || nameIndex >= strings.Count || strings[nameIndex] != "manifest") return null;

        var attributeStart = BitConverter.ToUInt16(data, body + 8);
        var attributeSize = BitConverter.ToUInt16(data, body + 10);
        var attributeCount = BitConverter.ToUInt16(data, body + 12);

        for (var i = 0; i < attributeCount; i++)
        {
            var attr = body + attributeStart + i * attributeSize;
            var attrName = BitConverter.ToInt32(data, attr + 4);
            if (attrName < 0 || attrName >= resourceIds.Length || resourceIds[attrName] != VersionCodeResourceId) continue;

            var dataType = data[attr + 15];
            if (dataType is TypeIntDec or TypeIntHex) return (int)BitConverter.ToUInt32(data, attr + 16);
        }

        return null;
    }

    private static List<string> ReadStringPool(byte[] data, int chunk)
    {
        var headerSize = BitConverter.ToUInt16(data, chunk + 2);
        var count = (int)BitConverter.ToUInt32(data, chunk + 8);
        var flags = BitConverter.ToUInt32(data, chunk + 16);
        var stringsStart = (int)BitConverter.ToUInt32(data, chunk + 20);
        var utf8 = (flags & 0x100) != 0;

        var result = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var stringOffset = (int)BitConverter.ToUInt32(data, chunk + headerSize + i * 4);
            var pos = chunk + stringsStart + stringOffset;
            result.Add(utf8 ? ReadUtf8(data, pos) : ReadUtf16(data, pos));
        }

        return result;
    }

    private static string ReadUtf16(byte[] data, int pos)
    {
        int length = BitConverter.ToUInt16(data, pos);
        pos += 2;
        if ((length & 0x8000) != 0)
        {
            length = ((length & 0x7FFF) << 16) | BitConverter.ToUInt16(data, pos);
            pos += 2;
        }

        return Encoding.Unicode.GetString(data, pos, length * 2);
    }

    private static string ReadUtf8(byte[] data, int pos)
    {
        // Dos longitudes: la de caracteres y la de bytes; cada una ocupa 1 byte, o 2 si el primero trae el bit alto.
        pos += (data[pos] & 0x80) != 0 ? 2 : 1;
        int byteLength = data[pos];
        pos++;
        if ((byteLength & 0x80) != 0)
        {
            byteLength = ((byteLength & 0x7F) << 8) | data[pos];
            pos++;
        }

        return Encoding.UTF8.GetString(data, pos, byteLength);
    }
}
