using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Pcs7CfcReader
{
    /// <summary>
    /// Read-only access to the PCS 7 CFC database through s7jdbmox.dll (undocumented C API, signatures reverse engineered
    /// on CFC V10.0 SP1). Every read runs inside one transaction that is always aborted, never committed.
    /// </summary>
    internal sealed unsafe class CfcDb : IDisposable
    {
        private const string Dll = "s7jdbmox.dll";
        private const int ModeFirst = 1, ModeNext = 2;
        private const int ParaFilterAll = 1;          // srv_SelectParas: all I/Os incl. structure members, values and interconnections
        private const int AllSheets = 0xFFFF;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_RegisterApp();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_TaskConnect(IntPtr taskName);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_TransactionBegin(ushort* session, ushort inSession);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_TransactionAbort(ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "?gl_ProjectIdGet@@YA_KPBDAA_KG@Z")]
        private static extern ulong gl_ProjectIdGet(byte* path, out ulong id, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "?gl_CpusGet@@YA_KW4MODE@@_KAA_KAAUOutCpu@@G@Z")]
        private static extern ulong gl_CpusGet(int mode, ulong parent, out ulong cpu, byte* outCpu, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "?gl_ChartsGet@@YA_KW4MODE@@_KAAUOutChart@@AA_KG@Z")]
        private static extern ulong gl_ChartsGet(int mode, ulong parent, byte* outChart, out ulong chart, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_ObjectCommentGet(ulong id, byte* comment, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong gl_ParaValueGet2(ulong id, byte* value, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong iea_CfcObjectsGetV6_0(int mode, ulong chart, int sheet, ulong* obj, byte* outObj, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong iea_ParasGetV6_0(int mode, ulong block, int filter, ulong* para, byte* outPara, ushort session);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] private static extern ulong iea_ConnectedParaPathGetV6_1(int mode, ulong para, ulong* other, byte* outPath, ushort session);

        private static readonly Encoding Ansi = Encoding.Default;
        private readonly ushort session;
        private bool open;

        public CfcDb()
        {
            Check("gl_RegisterApp", gl_RegisterApp());
            var name = Marshal.StringToHGlobalAnsi("Pcs7CfcReader");
            Check("gl_TaskConnect", gl_TaskConnect(name));
            ushort s = 0xFFFF;
            Check("gl_TransactionBegin", gl_TransactionBegin(&s, 0xFFFF));
            session = s;
            open = true;
        }

        public void Dispose()
        {
            if (open) { gl_TransactionAbort(session); open = false; }
        }

        public ulong ProjectId(string dbPath)
        {
            var bytes = Ansi.GetBytes(dbPath + "\0");
            fixed (byte* p = bytes)
            {
                var rc = gl_ProjectIdGet(p, out var id, session);
                if (id == 0) throw new CfcDbException("gl_ProjectIdGet", rc, "not a CFC database folder: " + dbPath);
                return id;
            }
        }

        public List<(ulong Id, string Name)> Cpus(ulong project)
        {
            var list = new List<(ulong, string)>();
            var buf = new byte[4096];
            fixed (byte* p = buf)
            {
                for (int mode = ModeFirst; list.Count < 1000; mode = ModeNext)
                {
                    Array.Clear(buf, 0, buf.Length);
                    var rc = gl_CpusGet(mode, project, out var id, p, session);
                    if (id == 0) break;
                    list.Add((id, Str(buf, 0, 0x124)));
                    if (rc != 0) break;               // last element comes with a non-zero status
                }
            }
            return list;
        }

        public List<(ulong Id, string Name)> Charts(ulong cpu)
        {
            var list = new List<(ulong, string)>();
            var buf = new byte[4096];
            fixed (byte* p = buf)
            {
                for (int mode = ModeFirst; list.Count < 100000; mode = ModeNext)
                {
                    Array.Clear(buf, 0, buf.Length);
                    var rc = gl_ChartsGet(mode, cpu, p, out var id, session);
                    if (id == 0) break;
                    list.Add((id, Str(buf, 4, 64)));
                    if (rc != 0) break;
                }
            }
            return list;
        }

        public string Comment(ulong id)
        {
            var buf = new byte[1024];
            fixed (byte* p = buf) { return gl_ObjectCommentGet(id, p, session) == 0 ? Str(buf, 0, 0x101) : null; }
        }

        public List<CfcObject> Objects(ulong chart)
        {
            var list = new List<CfcObject>();
            var buf = new byte[4096];
            fixed (byte* p = buf)
            {
                for (int mode = ModeFirst; list.Count < 100000; mode = ModeNext)
                {
                    Array.Clear(buf, 0, buf.Length);
                    ulong id = 0;
                    var rc = iea_CfcObjectsGetV6_0(mode, chart, AllSheets, &id, p, session);
                    if (id == 0) break;
                    list.Add(new CfcObject
                    {
                        Id = id,
                        Kind = BitConverter.ToInt32(buf, 0),
                        Type = Str(buf, 0x04, 0x20),
                        Name = Str(buf, 0x24, 0x28),
                        Comment = Str(buf, 0x4C, 0xE4 - 0x4C),
                    });
                    if (rc != 0) break;
                }
            }
            return list;
        }

        public List<CfcParameter> Parameters(ulong block, bool withSinks)
        {
            var list = new List<CfcParameter>();
            var buf = new byte[0x1000];
            var val = new byte[1024];
            fixed (byte* p = buf) fixed (byte* pv = val)
            {
                for (int mode = ModeFirst; list.Count < 100000; mode = ModeNext)
                {
                    Array.Clear(buf, 0, buf.Length);
                    ulong id = 0;
                    var rc = iea_ParasGetV6_0(mode, block, ParaFilterAll, &id, p, session);
                    if (id == 0) break;
                    var flags = buf[0x120];
                    var par = new CfcParameter
                    {
                        Id = id,
                        Name = Str(buf, 0x1F, 0x100) + Str(buf, 0x06, 0x19),
                        TypeCode = buf[0x000],
                        Direction = buf[0x234] == 1 ? "IN" : buf[0x234] == 2 ? "OUT" : buf[0x234] == 3 ? "IN_OUT" : buf[0x234].ToString(),
                        IsStructure = buf[0x000] == 0,
                        IsMember = buf[0x23C] == 0,
                        Visible = buf[0x240] != 0,
                        Changed = (flags & 0x02) != 0,
                        Connected = (flags & 0x04) != 0,
                        Value = Str(buf, 0x130, 0x190),
                        Comment = Str(buf, 0x3C1, 0x51)?.TrimEnd(),
                        TagSymbol = Str(buf, 0x444, 0x32),
                        TagAddress = Str(buf, 0x476, 0x32),
                        TagComment = Str(buf, 0x4A8, 0x80),
                        Source = Str(buf, 0x5D0, 0x310),
                    };
                    if (!par.IsStructure && string.IsNullOrEmpty(par.Value))
                    {
                        Array.Clear(val, 0, val.Length);
                        if (gl_ParaValueGet2(id, pv, session) == 0) par.Value = Str(val, 0, 512);
                    }
                    if (par.Direction == "OUT")
                    {
                        // For outputs the record holds only one partner; read the complete sink list instead.
                        par.Source = null;
                        if (withSinks) par.Sinks = Sinks(id);
                        if (par.Sinks != null && par.Sinks.Count > 0) par.Connected = true;
                    }
                    list.Add(par);
                    if (rc != 0) break;
                }
            }
            return list;
        }

        private List<string> Sinks(ulong para)
        {
            var list = new List<string>();
            var buf = new byte[0x1000];
            fixed (byte* p = buf)
            {
                for (int mode = ModeFirst; list.Count < 1000; mode = ModeNext)
                {
                    Array.Clear(buf, 0, buf.Length);
                    ulong other = 0;
                    var rc = iea_ConnectedParaPathGetV6_1(mode, para, &other, p, session);
                    if (other == 0) break;
                    list.Add($"{Str(buf, 0x118, 0x100)}\\{Str(buf, 0x101, 0x17)}.{Str(buf, 0, 0x101)}");
                    if (rc != 0) break;
                }
            }
            return list;
        }

        private static string Str(byte[] b, int offset, int max)
        {
            int end = offset;
            int limit = Math.Min(b.Length, offset + max);
            while (end < limit && b[end] != 0) end++;
            return end == offset ? null : Ansi.GetString(b, offset, end - offset);
        }

        private static void Check(string function, ulong rc)
        {
            if (rc != 0) throw new CfcDbException(function, rc, null);
        }
    }

    internal sealed class CfcDbException : Exception
    {
        public CfcDbException(string function, ulong rc, string detail)
            : base($"{function} failed (0x{rc:X16}){(detail == null ? "" : ": " + detail)}") { }
    }

    internal sealed class CfcObject
    {
        public ulong Id;
        public int Kind;
        public string Type, Name, Comment;
        public List<CfcParameter> Parameters;
    }

    internal sealed class CfcParameter
    {
        public ulong Id;
        public string Name, Direction, Value, Comment, TagSymbol, TagAddress, TagComment, Source;
        public int TypeCode;
        public bool IsStructure, IsMember, Visible, Changed, Connected;
        public List<string> Sinks;
    }
}

