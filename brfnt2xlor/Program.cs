using System.Text;
using System.Xml;

namespace brfnt2xlor
{
    class Program
    {
        static string outputName = "";
        static SortedDictionary<ushort, string> chars;

        static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("brfnt2xlor [file.brfnt] [output.xlor]");
                return;
            }

            if (!File.Exists(args[0]))
            {
                Console.WriteLine("File not found: " + args[0]);
                return;
            }

            outputName = Path.GetFileNameWithoutExtension(args[1]);
            decode(args[0], args[1]);
        }

        static void decode(string inputPath, string outputPath)
        {
            EndianReader reader = new EndianReader(File.Open(inputPath, FileMode.Open), Endianness.BigEndian);
            string fileMagic = reader.ReadString(4);
            if (fileMagic != "RFNT")
            {
                Console.WriteLine("Invalid File Magic: " + fileMagic);
                return;
            }

            ushort fileBOM = reader.ReadUInt16();
            if (fileBOM != 0xFEFF)
            {
                Console.WriteLine("Invalid Byte Order Mark: 0x" + fileBOM.ToString("X4"));
                return;
            }

            byte versionMajor = reader.ReadByte(); // 1
            byte versionMinor = reader.ReadByte(); // 4
            int fileLength = reader.ReadInt32();
            ushort headerLength = reader.ReadUInt16();
            ushort sectionCount = reader.ReadUInt16();

            #region FINF
            reader.Position = headerLength;
            string FINFMagic = reader.ReadString(4);
            if (FINFMagic != "FINF")
            {
                Console.WriteLine("Invalid FINF Magic: " + FINFMagic);
                return;
            }

            reader.Position += 0xC;
            int TGLPOffset = reader.ReadInt32() - 8;
            reader.Position += 4;
            int CMAPOffset = reader.ReadInt32() - 8;
            #endregion

            #region TGLP
            reader.Position = TGLPOffset;
            string TGLPMagic = reader.ReadString(4);
            if (TGLPMagic != "TGLP")
            {
                Console.WriteLine("Invalid TGLP Magic: " + TGLPMagic);
                return;
            }

            reader.Position += 0x10;
            ushort glyphsPerRow = reader.ReadUInt16();
            ushort glyphsPerColumn = reader.ReadUInt16();
            #endregion

            #region CMAP
            reader.Position = CMAPOffset;
            chars = new SortedDictionary<ushort, string>();

            readCMAP(reader);
            #endregion

            reader.Close();

            #region Write XLOR
            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings() { Indent = true };
            XmlWriter writer = XmlWriter.Create(outputPath, xmlWriterSettings);
            writer.WriteStartDocument();
            writer.WriteDocType("letter-order", null, "letter-order.dtd", null);
            writer.WriteWhitespace("\n\n");
            writer.WriteStartElement("letter-order");
            writer.WriteAttributeString("version", "1.1");
            writer.WriteStartElement("head");
            writer.WriteStartElement("create");
            writer.WriteAttributeString("user", Environment.UserName);
            writer.WriteAttributeString("host", Environment.MachineName);
            writer.WriteAttributeString("date", DateTime.Now.ToString("s"));
            writer.WriteEndElement(); // create
            writer.WriteSimpleElement("title", outputName);
            writer.WriteStartElement("generator");
            writer.WriteAttributeString("name", "brfnt2xlor");
            writer.WriteAttributeString("version", "1, 0, 0, 0");
            writer.WriteEndElement(); // generator
            writer.WriteEndElement(); // head
            //writer.WriteWhitespace("\n\n");

            writer.WriteStartElement("body");
            writer.WriteStartElement("area");
            writer.WriteAttributeInt("width", glyphsPerRow);
            writer.WriteAttributeInt("height", glyphsPerColumn);
            writer.WriteEndElement(); // area

            //writer.WriteWhitespace("\n\n");
            writer.WriteStartElement("order");

            int totalPerImage = glyphsPerRow * glyphsPerColumn;
            List<string> charValues = chars.Values.ToList();
            int nrImages = (int)Math.Ceiling((double)charValues.Count / (double)totalPerImage);
            int charId = 0;
            for (int i = 0; i < nrImages; i++)
            {
                writer.WriteComment("Image #" + (i + 1).ToString());
                writer.WriteWhitespace("\n");
                StringBuilder sb = new StringBuilder();
                for (int j = 0; j < glyphsPerColumn; j++)
                {
                    for (int k = 0; k < glyphsPerRow; k++)
                    {
                        if (charValues.Count > charId)
                        {
                            sb.Append(charValues[charId] + " ");
                            charId++;
                        }
                        else
                            sb.Append("<null/> ");
                    }
                    sb.Append("\n");
                }
                writer.WriteRaw(sb.ToString());
                writer.WriteWhitespace("\n\n");
            }

            writer.WriteEndElement(); // order
            writer.WriteEndElement(); // body
            writer.WriteEndElement(); // letter-order
            writer.Close();
            #endregion
        }

        static void readCMAP(EndianReader reader)
        {
            string CMAPMagic = reader.ReadString(4);
            if (CMAPMagic != "CMAP")
            {
                Console.WriteLine("Invalid CMAP Magic: " + CMAPMagic);
                return;
            }

            reader.Position += 4;
            ushort firstChar = reader.ReadUInt16();
            ushort lastChar = reader.ReadUInt16();
            ushort mapping = reader.ReadUInt16();
            reader.Position += 2;
            int nextCMAP = reader.ReadInt32() - 8;

            switch (mapping)
            {
                case 0: // Direct
                    ushort currentId = reader.ReadUInt16();
                    for (ushort i = firstChar; i < lastChar + 1; i++)
                    {
                        addChar(currentId, i);
                        currentId++;
                    }
                    break;

                case 1: // Table
                    for (ushort i = firstChar; i < lastChar + 1; i++)
                    {
                        currentId = reader.ReadUInt16();
                        if (currentId != 0xFFFF)
                            addChar(currentId, i);
                    }
                    break;

                case 2: // Scan
                    ushort nrChars = reader.ReadUInt16();
                    for (ushort i = 0; i < nrChars; i++)
                    {
                        ushort character = reader.ReadUInt16();
                        currentId = reader.ReadUInt16();
                        addChar(currentId, character);
                    }
                    break;
            }

            if (nextCMAP > 0)
            {
                reader.Position = nextCMAP;
                readCMAP(reader);
            }
        }

        static void addChar(ushort currentId, ushort code)
        {
            string codeStr = "";
            switch ((char)code)
            {
                case '&':
                    codeStr = "&amp;";
                    break;

                case '<':
                    codeStr = "&lt;";
                    break;

                case '>':
                    codeStr = "&gt;";
                    break;

                case ' ':
                    codeStr = "<sp/>";
                    break;

                case '\'':
                    codeStr = "&apos;";
                    break;

                case '"':
                    codeStr = "&quot;";
                    break;

                case '\u00a0':
                    codeStr = "&#x00A0;";
                    break;

                default:
                    codeStr = ((char)code).ToString();
                    break;
            }

            if (!string.IsNullOrEmpty(codeStr))
                chars.Add(currentId, codeStr);
        }
    }
}