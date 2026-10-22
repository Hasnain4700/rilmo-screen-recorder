// SPDX-License-Identifier: GPL-2.0-or-later
// Build-time only: read normal and delay-load DLL imports without loading binaries.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class RilmoPeImports
{
    public static string[] Read(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        uint pe = U32(bytes, 0x3c);
        if (U32(bytes, pe) != 0x00004550) throw new InvalidDataException("Not a PE image: " + path);
        ushort count = U16(bytes, pe + 6), optionalSize = U16(bytes, pe + 20);
        uint optional = pe + 24;
        ushort magic = U16(bytes, optional);
        if (magic != 0x20b && magic != 0x10b) throw new InvalidDataException("Unsupported PE optional header.");
        ulong imageBase = magic == 0x20b ? U64(bytes, optional + 24) : U32(bytes, optional + 28);
        uint directories = optional + (magic == 0x20b ? 112u : 96u);
        uint sectionTable = optional + optionalSize;
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (int index in new[] { 1, 13 })
        {
            uint rva = U32(bytes, directories + (uint)index * 8);
            uint size = U32(bytes, directories + (uint)index * 8 + 4);
            if (rva == 0 || size == 0) continue;
            uint table = Offset(bytes, rva, sectionTable, count);
            uint stride = index == 1 ? 20u : 32u;
            for (uint offset = 0; offset + stride <= size && offset < 1024 * 1024; offset += stride)
            {
                uint item = table + offset;
                uint name = U32(bytes, item + (index == 1 ? 12u : 4u));
                if (name == 0) break;
                if (index == 13 && (U32(bytes, item) & 1) == 0)
                {
                    if (name < imageBase || name - imageBase > uint.MaxValue) throw new InvalidDataException("Invalid delay import VA.");
                    name = (uint)(name - imageBase);
                }
                uint start = Offset(bytes, name, sectionTable, count), end = start;
                while (end < bytes.Length && end - start < 512 && bytes[end] != 0) end++;
                if (end == bytes.Length || end - start == 512) throw new InvalidDataException("Invalid import name.");
                string value = Encoding.ASCII.GetString(bytes, (int)start, (int)(end - start));
                if (value.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) throw new InvalidDataException("Unexpected import path.");
                result.Add(value);
            }
        }
        var values = new string[result.Count]; result.CopyTo(values); Array.Sort(values, StringComparer.OrdinalIgnoreCase); return values;
    }
    public static ushort Machine(string path)
    {
        byte[] bytes = File.ReadAllBytes(path); return U16(bytes, U32(bytes, 0x3c) + 4);
    }
    static uint Offset(byte[] bytes, uint rva, uint table, ushort count)
    {
        for (uint i = 0; i < count; i++)
        {
            uint section = table + i * 40, address = U32(bytes, section + 12);
            uint length = Math.Max(U32(bytes, section + 8), U32(bytes, section + 16));
            if (rva >= address && rva - address < length) return checked(U32(bytes, section + 20) + rva - address);
        }
        throw new InvalidDataException("Unmapped PE RVA.");
    }
    static ushort U16(byte[] bytes, uint offset) { Check(bytes, offset, 2); return BitConverter.ToUInt16(bytes, (int)offset); }
    static uint U32(byte[] bytes, uint offset) { Check(bytes, offset, 4); return BitConverter.ToUInt32(bytes, (int)offset); }
    static ulong U64(byte[] bytes, uint offset) { Check(bytes, offset, 8); return BitConverter.ToUInt64(bytes, (int)offset); }
    static void Check(byte[] bytes, uint offset, uint length) { if ((ulong)offset + length > (ulong)bytes.Length) throw new InvalidDataException("Truncated PE image."); }
}
