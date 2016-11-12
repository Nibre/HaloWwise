using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Reader = Zoltu.IO;

namespace HaloWwise
{
    class PackManager
    {
        public struct header
        {
            public UInt32 headerSize, folderListSize, bankTableSize, soundTableSize;
        }
        public struct folder
        {
            public UInt32 offset, id;
            public string name;
        }
        public struct soundBank
        {
            public UInt32 id, headerOffset, headerSize, hircOffset, hircSize;
            public string relativePath, idString;
        }
        public struct soundFile
        {
            public UInt32 id, fileOffset, fileSize;
            public string relativePath, idString;
        }

        private header _header = new header();
        private folder[] _folders;
        private Hashtable folderHashTable = new Hashtable();
        private List<soundBank> soundBankList = new List<soundBank>();
        private List<soundFile> soundFileList = new List<soundFile>();
        private List<string> conversionList = new List<string>();
        
        private string _packPath;
        private string _extractionPath;
        private BinaryReader binaryReader;
        private string baseName;
        private bool isBigEndian = false;
        private bool convertAfterExtraction;

        public PackManager(string packPath, string extractionPath)
        {
            _packPath = packPath;
            _extractionPath = extractionPath;
            baseName = Path.GetFileNameWithoutExtension(_packPath);
            binaryReader = new BinaryReader(File.Open(_packPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            convertAfterExtraction = ( File.Exists("ww2ogg.exe") && ( File.Exists("revorb.exe") && File.Exists("packed_codebooks_aoTuV_603.bin") ) );
        }

        public void ExtractPack()
        {
            if (ParseHeader(binaryReader))
            {
                GetFolders(binaryReader);
                GatherSoundBanks(binaryReader);
                GatherSoundFiles(binaryReader);

                Thread asyncExtraction = new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    AsyncExtractFiles();
                });
                asyncExtraction.Start();

                Thread asyncConversion = new Thread(() =>
                {
                    Thread.CurrentThread.IsBackground = true;
                    AsyncConvertFiles();
                });
                asyncConversion.Start();

                while (asyncExtraction.IsAlive || asyncConversion.IsAlive)
                    Thread.Sleep(1000); // Wait for extracting to finish, if it hasn't                
            }
        }

        private bool ParseHeader(BinaryReader br)
        {
            bool isPack = false;

            string ident = new string(br.ReadChars(4));
            if (ident == @"AKPK")
            {
                isPack = true;

                // Fix Endian-ness
                br.BaseStream.Seek(0x8, 0);
                if (br.ReadUInt32() != 1)
                {
                    binaryReader.Close();
                    binaryReader = new Reader.BigEndianBinaryReader(File.Open(_packPath, FileMode.Open, FileAccess.Read, FileShare.Read));
                    br = binaryReader;
                    isBigEndian = true;
                }
                br.BaseStream.Seek(0x4, 0);

                _header.headerSize = br.ReadUInt32();
                br.ReadUInt32(); // Skip
                _header.folderListSize = br.ReadUInt32();
                _header.bankTableSize = br.ReadUInt32();
                _header.soundTableSize = br.ReadUInt32();
                br.ReadUInt32(); // Skip
            }
            return isPack;
        }

        private void GetFolders(BinaryReader br)
        {
            UInt32 folderListStartPos = (UInt32)br.BaseStream.Position;
            UInt32 foldersCount = br.ReadUInt32();

            _folders = new folder[foldersCount];

            for (int i = 0; i < foldersCount; i++)
            {
                _folders[i].offset = br.ReadUInt32()+ folderListStartPos;
                _folders[i].id = br.ReadUInt32();

                UInt32 folderListTempPos = (UInt32)br.BaseStream.Position;

                // Go grab Folder name
                br.BaseStream.Seek(_folders[i].offset, 0);

                StringBuilder sb = new StringBuilder();
                while (br.PeekChar() != (char)0)
                {
                    sb.Append(br.ReadChar());
                    if (!isBigEndian)
                        br.ReadChar(); // H5 apparently has an extra 00 between Chars
                }
                _folders[i].name = sb.ToString();

                folderHashTable.Add(_folders[i].id, _folders[i].name);

                // Return to where we were in the List
                br.BaseStream.Seek(folderListTempPos, 0);
            }


            // Jump to past the Folder section
            br.BaseStream.Seek(folderListStartPos + _header.folderListSize, 0);
        }

        private void GatherSoundBanks(BinaryReader br)
        {
            UInt32 soundBankCount = br.ReadUInt32();

            for (int i = 0; i < soundBankCount; i++)
            {
                soundBank soundBank = new soundBank();

                // Sound Bank info
                soundBank.id = br.ReadUInt32();
                soundBank.idString = "0x"+ soundBank.id.ToString("X8");
                UInt32 soundBankOffsetMult = br.ReadUInt32();
                UInt32 soundBankSize = br.ReadUInt32();
                soundBank.headerOffset = br.ReadUInt32()*soundBankOffsetMult;
                UInt32 soundBankFolder = br.ReadUInt32();
                string soundBankFolderName = (string)folderHashTable[soundBankFolder];
                soundBank.relativePath = soundBankFolderName + "\\" + baseName + "\\" + "SoundBank (" + soundBank.idString + ")\\";

                UInt32 soundBankTempPos = (UInt32)br.BaseStream.Position;

                // Actual Sound Bank, header
                br.BaseStream.Seek(soundBank.headerOffset, 0);
                br.ReadUInt32(); // Bank head Identifier
                soundBank.headerSize = br.ReadUInt32() + 8; // Include 0x8 head
                UInt32 firstBankSectionPos = soundBank.headerOffset + soundBank.headerSize;
                br.BaseStream.Seek(firstBankSectionPos, 0);

                // Check if we have a DIDX section (contains embedded *.wem files) to deal with
                string firstSectionIdent = new string(br.ReadChars(4));
                if (firstSectionIdent == @"DIDX")
                {
                    //soundBank.relativePath += "SoundBank (" + soundBank.idString + ")\\"; // Move Bank+Sound into Bank folder

                    UInt32 didxSize = br.ReadUInt32();
                    UInt32 didxFilesCount = didxSize / 12; // Each file description is 0xC bytes
                    UInt32 dataFilesOffset = firstBankSectionPos + didxSize + 16; // 16 is for the DIDX+DATA headers

                    for(int j = 0; j < didxFilesCount; j++)
                    {
                        soundFile soundFile = new soundFile();

                        soundFile.id = br.ReadUInt32();
                        soundFile.idString = "0x" + soundFile.id.ToString("X8");
                        soundFile.fileOffset = br.ReadUInt32()+ dataFilesOffset;
                        soundFile.fileSize = br.ReadUInt32();

                        soundFile.relativePath = soundBank.relativePath;

                        soundFileList.Add(soundFile);
                    }

                    br.BaseStream.Seek(firstBankSectionPos + didxSize + 12, 0); // Get us to the DATA section size
                    UInt32 dataSectionSize = br.ReadUInt32();
                    br.ReadBytes((int)dataSectionSize + 4); // Skip to the end of DATA + 4, so we're back after HIRC Identifier
                }

                soundBank.hircOffset = (UInt32)br.BaseStream.Position - 4; // We already hit the HIRC head Identifier
                soundBank.hircSize = br.ReadUInt32() + 8; // Include 0x8 head

                // Go back to SoundBank list
                br.BaseStream.Seek(soundBankTempPos, 0);

                soundBankList.Add(soundBank);
            }
        }

        private void GatherSoundFiles(BinaryReader br)
        {
            UInt32 soundFileCount = br.ReadUInt32();

            for (int i = 0; i < soundFileCount; i++)
            {
                soundFile soundFile = new soundFile();

                soundFile.id = br.ReadUInt32();
                soundFile.idString = "0x" + soundFile.id.ToString("X8");
                UInt32 soundFileOffsetMult = br.ReadUInt32();
                soundFile.fileSize = br.ReadUInt32();
                soundFile.fileOffset = br.ReadUInt32() * soundFileOffsetMult;
                UInt32 soundFileFolder = br.ReadUInt32();
                string soundFileFolderName = (string)folderHashTable[soundFileFolder];
                soundFile.relativePath = soundFileFolderName + "\\" + baseName + "\\";

                soundFileList.Add(soundFile);
            }
        }

        /*
        private void ExtractFiles(BinaryReader br)
        {
            foreach(soundBank bank in soundBankList)
            {
                string bankExtractionPath = _extractionPath + "\\" + bank.relativePath + bank.idString + ".bnk";
                Directory.CreateDirectory(Path.GetDirectoryName(bankExtractionPath));
                using (FileStream writeStream = File.Create(bankExtractionPath))
                {
                    br.BaseStream.Seek(bank.headerOffset, 0);
                    byte[] headerBytes = new byte[bank.headerSize];
                    headerBytes = br.ReadBytes((int)bank.headerSize);

                    br.BaseStream.Seek(bank.hircOffset, 0);
                    byte[] hircBytes = new byte[bank.hircSize];
                    hircBytes = br.ReadBytes((int)bank.hircSize);
                    
                    writeStream.Write(headerBytes, 0, headerBytes.Length);
                    writeStream.Write(hircBytes, 0, hircBytes.Length);

                    writeStream.Close();
                }
            }

            foreach(soundFile file in soundFileList)
            {
                string fileExtractionPath = _extractionPath + "\\" + file.relativePath + file.idString + ".wem";
                Directory.CreateDirectory(Path.GetDirectoryName(fileExtractionPath));
                using (FileStream writeStream = File.Create(fileExtractionPath))
                {
                    br.BaseStream.Seek(file.fileOffset, 0);
                    byte[] fileBytes = new byte[file.fileSize];
                    fileBytes = br.ReadBytes((int)file.fileSize);
                    
                    writeStream.Write(fileBytes, 0, fileBytes.Length);
                    writeStream.Close();
                }
            }
        }*/

        private void AsyncExtractFiles()
        {
            var asyncBinaryReader = new BinaryReader(File.Open(_packPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            var br = asyncBinaryReader;

            while ((soundBankList.Count + soundFileList.Count) > 0 )
            {
                if (soundBankList.Count > 0)
                {
                    var bank = soundBankList.First();
                    string bankExtractionPath = _extractionPath + "\\" + bank.relativePath + bank.idString + ".bnk";
                    Directory.CreateDirectory(Path.GetDirectoryName(bankExtractionPath));
                    using (FileStream writeStream = File.Create(bankExtractionPath))
                    {
                        br.BaseStream.Seek(bank.headerOffset, 0);
                        byte[] headerBytes = new byte[bank.headerSize];
                        headerBytes = br.ReadBytes((int)bank.headerSize);

                        br.BaseStream.Seek(bank.hircOffset, 0);
                        byte[] hircBytes = new byte[bank.hircSize];
                        hircBytes = br.ReadBytes((int)bank.hircSize);

                        writeStream.Write(headerBytes, 0, headerBytes.Length);
                        writeStream.Write(hircBytes, 0, hircBytes.Length);

                        writeStream.Close();
                    }
                    // Do the Json conversion
                    var bankReader = new BankReader(bankExtractionPath, isBigEndian);

                    Trace.WriteLine("Extracted SoundBank " + bankExtractionPath.Replace(".bnk",".(bnk+json)"));
                    soundBankList.Remove(bank);
                }

                if (soundFileList.Count > 0)
                {
                    var file = soundFileList.First();
                    string fileExtractionPath = _extractionPath + "\\" + file.relativePath + "SoundFiles\\" + file.idString + ".wem";
                    Directory.CreateDirectory(Path.GetDirectoryName(fileExtractionPath));
                    using (FileStream writeStream = File.Create(fileExtractionPath))
                    {
                        br.BaseStream.Seek(file.fileOffset, 0);
                        byte[] fileBytes = new byte[file.fileSize];
                        fileBytes = br.ReadBytes((int)file.fileSize);

                        writeStream.Write(fileBytes, 0, fileBytes.Length);
                        writeStream.Close();
                    }

                    if (convertAfterExtraction && !isBigEndian) // H4 uses other codecs
                        conversionList.Add(fileExtractionPath);

                    Trace.WriteLine("Extracted SoundFile " + fileExtractionPath);
                    
                    soundFileList.Remove(file);
                }
            }
        }
        private void AsyncConvertFiles()
        {
            List<string> stageTwo = new List<string>();
            while ((conversionList.Count + soundBankList.Count + soundFileList.Count) > 0)
            {
                if (conversionList.Count > 0)
                {
                    var path = conversionList.ElementAt(0);
                    if (path == null) continue;

                    var wwToOgg = new Process();
                    wwToOgg.StartInfo.FileName = "ww2ogg.exe";
                    wwToOgg.StartInfo.Arguments = "--pcb packed_codebooks_aoTuV_603.bin \"" + path + "\"";
                    wwToOgg.StartInfo.CreateNoWindow = true;
                    wwToOgg.StartInfo.UseShellExecute = false;
                    wwToOgg.StartInfo.RedirectStandardError = true;
                    wwToOgg.StartInfo.RedirectStandardOutput = true;
                    wwToOgg.Start();

                    stageTwo.Add(path);
                    conversionList.Remove(path);
                }
            }

            Thread.Sleep(1000); // Helps revorb from stepping onto ww2ogg with Packs that have fewer sounds

            while (stageTwo.Count > 0)
            {
                var path = stageTwo.ElementAt(0);
                if (path == null) continue;

                var revorb = new Process();
                revorb.StartInfo.FileName = "revorb.exe";
                revorb.StartInfo.Arguments = "\""+path.Replace(".wem", ".ogg")+"\"";
                revorb.StartInfo.CreateNoWindow = true;
                revorb.StartInfo.UseShellExecute = false;
                revorb.StartInfo.RedirectStandardError = true;
                revorb.Start();

                revorb.WaitForExit();

                Trace.WriteLine("Converted SoundFile " + path + " to ogg");
                stageTwo.Remove(path);
            }
        }
    }
}
