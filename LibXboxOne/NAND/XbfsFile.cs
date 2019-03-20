using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace LibXboxOne.Nand
{
    public class XbfsFile
    {
        public static readonly int BlockSize = 0x1000;
        public static readonly int[] XbfsOffsets = { 0x10000, 0x810000, 0x820000 };
        public static string[] XbfsFilenames =
        {
            "1smcbl_a.bin", // 0
            "header.bin", // 1
            "devkit.ini", // 2
            "mtedata.cfg", // 3
            "certkeys.bin", // 4
            "smcerr.log", // 5
            "system.xvd", // 6
            "$sosrst.xvd", // 7
            "download.xvd", // 8
            "smc_s.cfg", // 9
            "sp_s.cfg", // 10, keyvault? has serial/partnum/osig, handled by psp.sys (/Device/psp)
            "os_s.cfg", // 11
            "smc_d.cfg", // 12
            "sp_d.cfg", // 13
            "os_d.cfg", // 14
            "smcfw.bin", // 15
            "boot.bin", // 16
            "host.xvd", // 17
            "settings.xvd", // 18
            "1smcbl_b.bin", // 19
            "bootanim.bin", // 20, this entry and ones below it are only in retail 97xx and above?
            "sostmpl.xvd", // 21
            "update.cfg", // 22
            "sosinit.xvd", // 23
            "hwinit.cfg", // 24
            "qaslt.xvd", // 25
            "keyvault.bin", // 26, keyvault backup? has serial/partnum/osig
            "unknown2.bin", // 27
            "unknown3.bin", // 28
            "unknownBlank2.bin" // 29
        };

        private readonly IO _io;
        private readonly string _filePath;

        public List<XbfsHeader> XbfsHeaders;
        public Certificates.PspConsoleCert ConsoleCert;

        public string FilePath
        {
            get { return _filePath; }
        }

        public XbfsFile(string path)
        {
            _filePath = path;
            _io = new IO(path);
        }

        public static long FromLBA(uint lba)
        {
            return lba * BlockSize;
        }

        public static uint ToLBA(long offset)
        {
            return (uint)(offset / BlockSize);
        }

        public bool Load()
        {
            // read each XBFS header
            XbfsHeaders = new List<XbfsHeader>();
            foreach (int offset in XbfsOffsets)
            {
                _io.Stream.Position = offset;
                var header = _io.Reader.ReadStruct<XbfsHeader>();
                XbfsHeaders.Add(header);
            }

            long spDataSize = SeekToFile("sp_s.cfg");
            if (spDataSize <= 0)
                return true;

            // SP_S.cfg: (secure processor secured config? there's also a blank sp_d.cfg which is probably secure processor decrypted config)
            // 0x0    - 0x200   - signature?
            // 0x200  - 0x5200  - encrypted data? maybe loaded and decrypted into PSP memory?
            // 0x5200 - 0x5400  - blank
            // 0x5400 - 0x5800  - console certificate
            // 0x5800 - 0x6000  - unknown data, looks like it has some hashes and the OSIG of the BR drive
            // 0x6000 - 0x6600  - encrypted data?
            // 0x6600 - 0x7400  - blank
            // 0x7400 - 0x7410  - unknown data, hash maybe
            // 0x7410 - 0x40000 - blank

            _io.Stream.Position += 0x5400; // seek to start of unencrypted data in sp_s (console certificate)
            ConsoleCert = _io.Reader.ReadStruct<Certificates.PspConsoleCert>();

            return true;
        }

        // returns the size of the file if found
        public long SeekToFile(string fileName)
        {
            int idx = Array.IndexOf(XbfsFilenames, fileName);
            if (idx < 0)
                return 0;
            long size = 0;
            for (int i = 0; i < XbfsHeaders.Count; i++)
            {
                if (!XbfsHeaders[i].IsValid)
                    continue;
                if (idx >= XbfsHeaders[i].Files.Length)
                    continue;
                var ent = XbfsHeaders[i].Files[idx];
                if (ent.Length == 0)
                    continue;
                _io.Stream.Position = FromLBA(ent.LBA);
                size = FromLBA(ent.Length);
            }
            return size;
        }

        public string GetXbfsInfo()
        {
            var info = new Dictionary<long, string>();
            for (int i = 0; i < XbfsHeaders.Count; i++)
            {
                if (!XbfsHeaders[i].IsValid)
                    continue;
                for (int y = 0; y < XbfsHeaders[i].Files.Length; y++)
                {
                    var ent = XbfsHeaders[i].Files[y];
                    if (ent.Length == 0)
                        continue;
                    long start = FromLBA(ent.LBA);
                    long length = FromLBA(ent.Length);
                    long end = start + length;
                    string addInfo = String.Format("{0:X} {1}_{2}", end, i, y);
                    if (info.ContainsKey(start))
                        info[start] += " " + addInfo;
                    else
                        info.Add(start, addInfo);
                }
            }
            string infoStr = String.Empty;
            var keys = info.Keys.ToList();
            keys.Sort();
            foreach (var key in keys)
                infoStr += "" + key.ToString("X") + " - " + info[key] + Environment.NewLine;

            return infoStr;
        }

        public void ExtractXbfsData(string folderPath)
        {
            var doneAddrs = new List<long>();
            for (int i = 0; i < XbfsHeaders.Count; i++)
            {
                if (!XbfsHeaders[i].IsValid)
                    continue;
                for (int y = 0; y < XbfsHeaders[i].Files.Length; y++)
                {
                    var ent = XbfsHeaders[i].Files[y];
                    if (ent.Length == 0)
                        continue;

                    string fileName = FromLBA(ent.LBA).ToString("X") + "_" + FromLBA(ent.Length).ToString("X") + "_" + i + "_" + y + "_" + XbfsFilenames[y];

                    long read = 0;
                    long total = FromLBA(ent.Length);
                    _io.Stream.Position = FromLBA(ent.LBA);

                    bool writeFile = true;
                    if (doneAddrs.Contains(_io.Stream.Position))
                    {
                        writeFile = false;
                        fileName = "DUPE_" + fileName;
                    }
                    doneAddrs.Add(_io.Stream.Position);


                    if (_io.Stream.Position + total > _io.Stream.Length)
                        continue;

                    using (var fileIo = new IO(Path.Combine(folderPath, fileName), FileMode.Create))
                    {
                        if(writeFile)
                            while (read < total)
                            {
                                int toRead = 0x4000;
                                if (total - read < toRead)
                                    toRead = (int) (total - read);
                                byte[] data = _io.Reader.ReadBytes(toRead);
                                fileIo.Writer.Write(data);
                                read += toRead;
                            }
                    }
                }
            }
        }


        public override string ToString()
        {
            return ToString(false);
        }

        public string ToString(bool formatted)
        {
            var b = new StringBuilder();
            b.AppendLine("XbfsFile");
            b.AppendLine();
            for (int i = 0; i < XbfsHeaders.Count; i++)
            {
                if(!XbfsHeaders[i].IsValid)
                    continue;

                b.AppendLine(String.Format("XbfsHeader slot {0}: (0x{1:X})", i, XbfsOffsets[i]));
                b.Append(XbfsHeaders[i].ToString(formatted));
            }
            return b.ToString();
        }
    }
}