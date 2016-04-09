﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using KFreonLib.MEDirectories;
using Newtonsoft.Json;

namespace ME3Explorer.Unreal
{
    public static class UnrealObjectInfo
    {
        public class PropertyInfo
        {
            public PropertyReader.Type type;
            public string reference;
        }

        public class ClassInfo
        {
            public Dictionary<string, PropertyInfo> properties;
            public string baseClass;

            public ClassInfo()
            {
                properties = new Dictionary<string, PropertyInfo>();
            }
        }

        public class SequenceObjectInfo
        {
            public List<string> inputLinks;

            public SequenceObjectInfo()
            {
                inputLinks = new List<string>();
            }
        }

        public static Dictionary<string, ClassInfo> Classes = new Dictionary<string, ClassInfo>();
        public static Dictionary<string, ClassInfo> Structs = new Dictionary<string, ClassInfo>();
        public static Dictionary<string, SequenceObjectInfo> SequenceObjects = new Dictionary<string, SequenceObjectInfo>();
        public static Dictionary<string, List<string>> Enums = new Dictionary<string, List<string>>();

        public static void loadfromJSON()
        {
            string path = Application.StartupPath + "//exec//ME3ObjectInfo.json";

            try
            {
                if (File.Exists(path))
                {
                    string raw = File.ReadAllText(path);
                    var blob  = JsonConvert.DeserializeAnonymousType(raw, new { SequenceObjects, Classes, Structs, Enums });
                    SequenceObjects = blob.SequenceObjects;
                    Classes = blob.Classes;
                    Structs = blob.Structs;
                    Enums = blob.Enums;
                }
            }
            catch (Exception)
            {
            }
        }

        public static SequenceObjectInfo getSequenceObjectInfo(string objectName)
        {
            if (objectName.StartsWith("Default__"))
            {
                objectName = objectName.Substring(9);
            }
            if (SequenceObjects.ContainsKey(objectName))
            {
                return SequenceObjects[objectName];
            }
            return null;
        }

        public static List<string> getEnumValues(string enumName)
        {
            if (Enums.ContainsKey(enumName))
            {
                return Enums[enumName];
            }
            return null;
        }

        #region Generating
        //call this method to regenerate ME3ObjectInfo.json
        //Takes a long time (5 to 10 minutes maybe?). Application will be completely unresponsive during that time.
        public static void generateInfo()
        {
            PCCObject pcc;
            string path = ME3Directory.gamePath;
            string[] files = Directory.GetFiles(path, "*.pcc", SearchOption.AllDirectories);
            string objectName;
            for (int i = 0; i < files.Length; i++)
            {
                if (files[i].ToLower().EndsWith(".pcc"))
                {
                    pcc = new PCCObject(files[i]);
                    for (int j = 0; j < pcc.Exports.Count; j++)
                    {
                        if (pcc.Exports[j].ClassName == "Enum")

                        {
                            generateEnumValues(j, pcc);
                        }
                        else if (pcc.Exports[j].ClassName == "Class")
                        {
                            objectName = pcc.Exports[j].ObjectName;
                            if (!Classes.ContainsKey(pcc.Exports[j].ObjectName))
                            {
                                Classes.Add(objectName, generateClassInfo(j, pcc));
                            }
                            if (pcc.Exports[j].ObjectName.Contains("Seq_Act") || pcc.Exports[j].ObjectName.Contains("Seq_Cond"))
                            {
                                SequenceObjects.Add(objectName, generateSequenceObjectInfo(j, pcc));
                                return;
                            }
                        }
                        else if (pcc.Exports[j].ClassName == "ScriptStruct")
                        {
                            objectName = pcc.Exports[j].ObjectName;
                            if (!Structs.ContainsKey(pcc.Exports[j].ObjectName))
                            {
                                Structs.Add(objectName, generateClassInfo(j, pcc));
                            }
                        }
                    }
                }
            }
            //populate SequenceObjects with inherited input links
            foreach (KeyValuePair<string, SequenceObjectInfo> pair in SequenceObjects)
            {
                if(pair.Value.inputLinks.Count == 0)
                {
                    SequenceObjectInfo s;
                    string baseClass = Classes[pair.Key].baseClass;
                    while (baseClass != "Class")
                    {
                        s = SequenceObjects[baseClass];
                        if (s.inputLinks.Count > 0)
                        {
                            SequenceObjects[pair.Key] = pair.Value;
                            break;
                        }
                        baseClass = Classes[baseClass].baseClass;
                    }
                }
            }
            File.WriteAllText(Application.StartupPath + "//exec//ME3ObjectInfo.json", JsonConvert.SerializeObject(new { SequenceObjects = SequenceObjects, Classes = Classes, Structs = Structs, Enums = Enums }));
            MessageBox.Show("Done");
        }

        private static SequenceObjectInfo generateSequenceObjectInfo(int i, PCCObject pcc)
        {
            SequenceObjectInfo info = new SequenceObjectInfo();
            PropertyReader.Property inputLinks = PropertyReader.getPropOrNull(pcc, pcc.Exports[i + 1], "InputLinks");
            if (inputLinks != null)
            {
                int pos = 28;
                byte[] global = inputLinks.raw;
                int count = BitConverter.ToInt32(global, 24);
                for (int j = 0; j < count; j++)
                {
                    List<PropertyReader.Property> p2 = PropertyReader.ReadProp(pcc, global, pos);

                    info.inputLinks.Add(p2[0].Value.StringValue);
                    for (int k = 0; k < p2.Count(); k++)
                        pos += p2[k].raw.Length;
                }
            }
            return info;
        }

        private static ClassInfo generateClassInfo(int index, PCCObject pcc)
        {
            ClassInfo info = new ClassInfo();
            info.baseClass = pcc.Exports[index].ClassParent;
            foreach (PCCObject.ExportEntry entry in pcc.Exports)
            {
                if (entry.idxLink - 1 == index && entry.ClassName != "ScriptStruct" && entry.ClassName != "Enum"
                    && entry.ClassName != "Function" && entry.ClassName != "Const" && entry.ClassName != "State")
                {
                    //Skip if property is transient (only used during execution, will never be in game files)
                    if ((BitConverter.ToUInt64(entry.Data, 24) & 0x0000000000002000) == 0 && !info.properties.ContainsKey(entry.ObjectName))
                    {
                        PropertyInfo p = getProperty(pcc, entry);
                        if (p != null)
                        {
                            info.properties.Add(entry.ObjectName, p);
                        }
                    }
                }
            }
            return info;
        }

        private static void generateEnumValues(int index, PCCObject pcc)
        {
            string enumName = pcc.Exports[index].ObjectName;
            if (!Enums.ContainsKey(enumName))
            {
                List<string> values = new List<string>();
                byte[] buff = pcc.Exports[index].Data;
                int count = BitConverter.ToInt32(buff, 20);
                for (int i = 0; i < count; i++)
                {
                    values.Add(pcc.Names[BitConverter.ToInt32(buff, 24 + i * 8)]);
                }
                Enums.Add(enumName, values);
            }
        }

        private static PropertyInfo getProperty(PCCObject pcc, PCCObject.ExportEntry entry)
        {
            PropertyInfo p = new PropertyInfo();
            switch (entry.ClassName)
            {
                case "IntProperty":
                    p.type = PropertyReader.Type.IntProperty;
                    break;
                case "StringRefProperty":
                    p.type = PropertyReader.Type.StringRefProperty;
                    break;
                case "FloatProperty":
                    p.type = PropertyReader.Type.FloatProperty;
                    break;
                case "BoolProperty":
                    p.type = PropertyReader.Type.BoolProperty;
                    break;
                case "StrProperty":
                    p.type = PropertyReader.Type.StrProperty;
                    break;
                case "NameProperty":
                    p.type = PropertyReader.Type.NameProperty;
                    break;
                case "DelegateProperty":
                    p.type = PropertyReader.Type.DelegateProperty;
                    break;
                case "ObjectProperty":
                    p.type = PropertyReader.Type.ObjectProperty;
                    p.reference = pcc.getObjectName(BitConverter.ToInt32(entry.Data, entry.Data.Length - 4));
                    break;
                case "StructProperty":
                    p.type = PropertyReader.Type.StructProperty;
                    p.reference = pcc.getObjectName(BitConverter.ToInt32(entry.Data, entry.Data.Length - 4));
                    break;
                case "BioMask4Property":
                case "ByteProperty":
                    p.type = PropertyReader.Type.ByteProperty;
                    p.reference = pcc.getObjectName(BitConverter.ToInt32(entry.Data, entry.Data.Length - 4));
                    break;
                case "ArrayProperty":
                    p.type = PropertyReader.Type.ArrayProperty;
                    PropertyInfo arrayTypeProp = getProperty(pcc, pcc.Exports[BitConverter.ToInt32(entry.Data, 44) - 1]);
                    if (arrayTypeProp != null)
                    {
                        switch (arrayTypeProp.type)
                        {
                            case PropertyReader.Type.ObjectProperty:
                            case PropertyReader.Type.StructProperty:
                            case PropertyReader.Type.ArrayProperty:
                                p.reference = arrayTypeProp.reference;
                                break;
                            case PropertyReader.Type.ByteProperty:
                                if (arrayTypeProp.reference == "")
                                    p.reference = arrayTypeProp.type.ToString();
                                else
                                    p.reference = arrayTypeProp.reference;
                                break;
                            case PropertyReader.Type.IntProperty:
                            case PropertyReader.Type.FloatProperty:
                            case PropertyReader.Type.NameProperty:
                            case PropertyReader.Type.BoolProperty:
                            case PropertyReader.Type.StrProperty:
                            case PropertyReader.Type.StringRefProperty:
                            case PropertyReader.Type.DelegateProperty:
                                p.reference = arrayTypeProp.type.ToString();
                                break;
                            case PropertyReader.Type.None:
                            case PropertyReader.Type.Unknown:
                            default:
                                System.Diagnostics.Debugger.Break();
                                p = null;
                                break;
                        }
                    }
                    else
                    {
                        p = null;
                    }
                    break;
                case "ClassProperty":
                case "InterfaceProperty":
                case "ComponentProperty":
                default:
                    p = null;
                    break;
            }

            return p;
        }
        #endregion
    }
}
