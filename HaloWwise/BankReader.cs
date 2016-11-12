using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Reader = Zoltu.IO;
using Newtonsoft.Json;

namespace HaloWwise
{
    class BankReader
    {
        private string _bankPath;
        private BinaryReader binaryReader;
        private bool _isBigEndian;

        public struct header
        {
            public UInt32 length, version, id, unknown1, unknown2;
        }
        public struct hirc
        {
            public struct wwiseObject
            {
                public enum wwiseObjectType
                {
                    Settings = 1, Sound = 2, EventAction=3, Event=4,SequenceContainer=5,
                    SwitchContainer = 6, ActorMixer = 7,AudioBus = 8,BlendContainer = 9,MusicSegment = 10,
                    MusicTrack = 11, MusicSwitchContainer = 12, MusicPlaylistContainer = 13, Attenuation = 14, DialogueEvent = 15,
                    MotionBus = 16, MotionFX = 17, Effect = 18, AuxillaryBus = 20
                }
                public wwiseObjectType type;
                public UInt32 length, id;
                public byte[] otherBytes;

                public struct wwiseSoundObject
                {
                    public UInt32 soundFileID;
                    // More data left, if that matters
                }
                public struct wwiseEventActionData
                {
                    public byte eventActionScope, eventActionType;
                    public UInt32 soundObjectID;
                    // More data left, if that matters
                }
                public struct wwiseEventData
                {
                    public UInt32 eventActionCount;
                    public UInt32[] eventActions;
                }
                public wwiseSoundObject soundObject;
                public wwiseEventActionData eventActionData;
                public wwiseEventData eventData;
            }

            public UInt32 length, objectCount;
            public List<wwiseObject> objects;
        }

        private header _header = new header();
        private hirc _hirc = new hirc();
        private Dictionary<hirc.wwiseObject.wwiseObjectType,Int32> hircStats = new Dictionary<hirc.wwiseObject.wwiseObjectType, int>();

        public BankReader(string bankPath, bool isBigEndian)
        {
            _bankPath = bankPath;
            _isBigEndian = isBigEndian;
            if (isBigEndian)
                binaryReader = new Reader.BigEndianBinaryReader(File.Open(_bankPath, FileMode.Open, FileAccess.Read, FileShare.Read));
            else
                binaryReader = new BinaryReader(File.Open(_bankPath, FileMode.Open, FileAccess.Read, FileShare.Read));

            ParseBank();
        }


        private void ParseBank()
        {
            if (ParseHeader(binaryReader)){
                ParseHirc(binaryReader);                
                //File.WriteAllText(_bankPath.Replace(".bnk", ".json"), JsonConvert.SerializeObject(_hirc, Formatting.Indented));
                StringBuilder sb = new StringBuilder();
                StringWriter sw = new StringWriter(sb);

                using (JsonWriter writer = new JsonTextWriter(sw))
                {
                    writer.Formatting = Formatting.Indented;

                    writer.WriteStartObject();
                    writer.WritePropertyName("Objects");
                    writer.WriteStartArray();
                    if (_hirc.objects != null)
                    {
                        foreach (hirc.wwiseObject hircObject in _hirc.objects)
                        {
                            writer.WriteStartObject();
                            writer.WritePropertyName("Type");
                            writer.WriteValue(hircObject.type.ToString());
                            writer.WritePropertyName("Length");
                            writer.WriteValue(hircObject.length);
                            writer.WritePropertyName("ID");
                            writer.WriteValue("0x" + hircObject.id.ToString("X8"));
                            switch (hircObject.type)
                            {
                                case hirc.wwiseObject.wwiseObjectType.Sound:
                                    writer.WritePropertyName("soundFileID");
                                    writer.WriteValue("0x" + hircObject.soundObject.soundFileID.ToString("X8"));
                                    break;
                                case hirc.wwiseObject.wwiseObjectType.EventAction:
                                    writer.WritePropertyName("eventActionScope");
                                    writer.WriteValue(hircObject.eventActionData.eventActionScope);
                                    writer.WritePropertyName("eventActionType");
                                    writer.WriteValue(hircObject.eventActionData.eventActionType);
                                    writer.WritePropertyName("soundObjectID");
                                    writer.WriteValue("0x" + hircObject.eventActionData.soundObjectID.ToString("X8"));
                                    break;
                                case hirc.wwiseObject.wwiseObjectType.Event:
                                    writer.WritePropertyName("eventActions");
                                    writer.WriteStartArray();
                                    foreach (UInt32 eventActionID in hircObject.eventData.eventActions)
                                        writer.WriteValue("0x" + eventActionID.ToString("X8"));
                                    writer.WriteEndArray();
                                    break;
                            }

                            writer.WritePropertyName("otherBytes");
                            writer.WriteValue(BitConverter.ToString(hircObject.otherBytes));
                            writer.WriteEndObject();
                        }
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                File.WriteAllText(_bankPath.Replace(".bnk", ".json"), sb.ToString());
                //Trace.WriteLine("Converted SoundBank " + _bankPath + " to JSON");

                /*foreach (KeyValuePair<hirc.wwiseObject.wwiseObjectType,Int32> kvp in hircStats)
                {
                    if (kvp.Key == hirc.wwiseObject.wwiseObjectType.Sound)
                        Debug.Write(kvp.Key.ToString()+" "+kvp.Value.ToString()+" - ");
                }
                Trace.Write(_bankPath + "\n");*/
            }
        }
        private bool ParseHeader(BinaryReader br)
        {
            bool isBank = false;

            string ident = new string(br.ReadChars(4));
            if (ident == @"BKHD")
            {
                isBank = true;

                _header.length = br.ReadUInt32();
                _header.version = br.ReadUInt32();
                _header.id = br.ReadUInt32();
                _header.unknown1 = br.ReadUInt32();
                _header.unknown2 = br.ReadUInt32();

                br.BaseStream.Seek(_header.length + 8, 0); // Skip to past the header, incl. its Ident+Size
            }

            return isBank;
        }
        private void ParseHirc(BinaryReader br)
        {
            string ident = new string(br.ReadChars(4));
            if (ident != @"HIRC")
                return; // Empty Bank

        _hirc.objects = new List<hirc.wwiseObject>();
        _hirc.length = br.ReadUInt32();
            _hirc.objectCount = br.ReadUInt32();

            for (int i = 0; i < _hirc.objectCount; i++)
            {
                var wwiseObject = new hirc.wwiseObject();
                wwiseObject.type = (hirc.wwiseObject.wwiseObjectType)br.ReadByte();
                wwiseObject.length = br.ReadUInt32();
                wwiseObject.id = br.ReadUInt32();
                var otherBytesPos = br.BaseStream.Position;
                wwiseObject.otherBytes = br.ReadBytes((int)wwiseObject.length - 4); // -4 becase of the Id

                var tempPos = br.BaseStream.Position;

                switch (wwiseObject.type)
                {
                    case hirc.wwiseObject.wwiseObjectType.Sound:
                        wwiseObject.soundObject = new hirc.wwiseObject.wwiseSoundObject();

                        br.BaseStream.Seek(otherBytesPos, SeekOrigin.Begin);
                        br.ReadBytes(5); // 4 unknown + 1 SoundSource
                        wwiseObject.soundObject.soundFileID = br.ReadUInt32();

                        br.BaseStream.Seek(tempPos, SeekOrigin.Begin);
                        break;

                    case hirc.wwiseObject.wwiseObjectType.EventAction:
                        wwiseObject.eventActionData = new hirc.wwiseObject.wwiseEventActionData();

                        br.BaseStream.Seek(otherBytesPos, SeekOrigin.Begin);
                        wwiseObject.eventActionData.eventActionScope = br.ReadByte();
                        wwiseObject.eventActionData.eventActionType = br.ReadByte();
                        wwiseObject.eventActionData.soundObjectID = br.ReadUInt32();

                        br.BaseStream.Seek(tempPos, SeekOrigin.Begin);
                        break;

                    case hirc.wwiseObject.wwiseObjectType.Event:
                        wwiseObject.eventData = new hirc.wwiseObject.wwiseEventData();

                        br.BaseStream.Seek(otherBytesPos, SeekOrigin.Begin);
                        wwiseObject.eventData.eventActionCount = br.ReadUInt32();
                        wwiseObject.eventData.eventActions = new UInt32[wwiseObject.eventData.eventActionCount];
                        for(int action=0; action < wwiseObject.eventData.eventActionCount; action++)
                            wwiseObject.eventData.eventActions[action] = br.ReadUInt32();

                        br.BaseStream.Seek(tempPos, SeekOrigin.Begin);
                        break;
                }

                _hirc.objects.Add(wwiseObject);
                if (hircStats.ContainsKey(wwiseObject.type))
                    hircStats[wwiseObject.type] += 1;
                else
                    hircStats[wwiseObject.type] = 1;
            }
        }
    }
}
