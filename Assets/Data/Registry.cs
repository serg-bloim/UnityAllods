using System;
using System.IO;

public class Registry
{
    internal enum RegistryNodeType
    {
        Directory,
        String,
        Float,
        Int,
        Array,

        Null
    }

    internal class RegistryNode
    {
        public string Name = "";

        public RegistryNodeType Type = RegistryNodeType.Null;

        public string ValueS = null;
        public double ValueF = 0.0;
        public int ValueI = 0;
        public int[] ValueA = null;

        public RegistryNode[] Children = null;
    }

    static private readonly int RegistrySignature = 0x31415926;
    internal RegistryNode Root;

    private static bool TreeTraverse(MemoryStream ms, BinaryReader br, RegistryNode node, uint first, uint last, uint data_origin)
    {
        for (uint i = first; i < last; i++)
        {
            ms.Seek(0x18 + 0x20 * i, SeekOrigin.Begin);

            br.BaseStream.Position += 4; // uint e_unk1 = msb.ReadUInt32();
            uint e_offset = br.ReadUInt32(); // 0x1C
            uint e_count = br.ReadUInt32(); // 0x18
            uint e_type = br.ReadUInt32();// 0x14
            string e_name = Core.UnpackByteString(866, br.ReadBytes(16));

            RegistryNode subnode = new RegistryNode();
            node.Children[i - first] = subnode;

            subnode.Name = e_name;
            if (e_type == 0) // string value
            {
                subnode.Type = RegistryNodeType.String;
                ms.Seek(data_origin + e_offset, SeekOrigin.Begin);
                subnode.ValueS = Core.UnpackByteString(866, br.ReadBytes((int)e_count));
            }
            else if (e_type == 2) // dword value
            {
                subnode.Type = RegistryNodeType.Int;
                subnode.ValueI = (int)e_offset;
            }
            else if (e_type == 4) // float value
            {
                // well, we gotta rewind and read it again
                // C-style union trickery won't work
                subnode.Type = RegistryNodeType.Float;
                ms.Seek(-0x1C, SeekOrigin.Current);
                subnode.ValueF = br.ReadDouble();
            }
            else if (e_type == 6) // int array
            {
                if ((e_count % 4) != 0)
                    return false;
                uint e_acount = e_count / 4;
                subnode.Type = RegistryNodeType.Array;
                subnode.ValueA = new int[e_acount];
                ms.Seek(data_origin + e_offset, SeekOrigin.Begin);
                for (uint j = 0; j < e_acount; j++)
                    subnode.ValueA[j] = br.ReadInt32();
            }
            else if (e_type == 1) // directory
            {
                subnode.Type = RegistryNodeType.Directory;
                subnode.Children = new RegistryNode[e_count];
                if (!TreeTraverse(ms, br, subnode, e_offset, e_offset + e_count, data_origin))
                    return false;
            }
        }

        return true;
    }

    public Registry(string filename)
    {
        try
        {
            MemoryStream ms = ResourceManager.OpenRead(filename);
            if (ms == null)
            {
                Core.Abort("Couldn't load \"{0}\"", filename);
                return;
            }

            BinaryReader br = new BinaryReader(ms);
            if (br.ReadUInt32() != RegistrySignature)
            {
                ms.Close();
                Core.Abort("Couldn't load \"{0}\" (not a registry file)", filename);
                return;
            }

            uint root_offset = br.ReadUInt32();
            uint root_size = br.ReadUInt32();
            br.BaseStream.Position += 4; // uint reg_flags = br.ReadUInt32();
            uint reg_eatsize = br.ReadUInt32();
            br.BaseStream.Position += 4; // uint reg_junk = msb.ReadUInt32();

            Root = new RegistryNode();
            Root.Type = RegistryNodeType.Directory;
            Root.Name = "";
            Root.Children = new RegistryNode[root_size];
            if (!TreeTraverse(ms, br, Root, root_offset, root_offset + root_size, 0x1C + 0x20 * reg_eatsize))
            {
                ms.Close();
                Core.Abort("Couldn't load \"{0}\" (invalid registry file)", filename);
                return;
            }

            ms.Close();
        }
        catch (IOException)
        {
            Core.Abort("Couldn't load \"{0}\"", filename);
        }
    }

    private RegistryNode GetRegistryNode(string name1, string name2)
    {
        RegistryNode level1 = null;
        RegistryNode level2 = null;

        foreach (RegistryNode node in Root.Children)
        {
            if (node.Name.ToLower().Equals(name1.ToLower()))
            {
                level1 = node;
                break;
            }
        }

        if ((level1 == null) ||
            (level1.Type != RegistryNodeType.Directory))
            return null;

        foreach (RegistryNode node in level1.Children)
        {
            if (node.Name.ToLower().Equals(name2.ToLower()))
            {
                level2 = node;
                break;
            }
        }

        return level2;
    }

    public string GetString(string name1, string name2, string def)
    {
        RegistryNode node = GetRegistryNode(name1, name2);
        if ((node == null) ||
            (node.Type != RegistryNodeType.String)) return def;
        return node.ValueS;
    }

    public int GetInt(string name1, string name2, int def)
    {
        RegistryNode node = GetRegistryNode(name1, name2);
        if ((node == null) ||
            (node.Type != RegistryNodeType.Int)) return def;
        return node.ValueI;
    }

    public double GetFloat(string name1, string name2, double def)
    {
        RegistryNode node = GetRegistryNode(name1, name2);
        if ((node == null) ||
            (node.Type != RegistryNodeType.Float)) return def;
        return node.ValueF;
    }

    public int[] GetArray(string name1, string name2, int[] def)
    {
        RegistryNode node = GetRegistryNode(name1, name2);
        if ((node == null) ||
            (node.Type != RegistryNodeType.Array)) return def;
        return (int[])node.ValueA.Clone();
    }

    public object GetGeneric(Type t, string name1, string name2, object def)
    {
        if (t == typeof(int))
            return GetInt(name1, name2, (int)def);
        else if (t == typeof(float) || t == typeof(double))
            return GetFloat(name1, name2, (double)def);
        else if (t == typeof(string))
            return GetString(name1, name2, (string)def);
        else if (t == typeof(int[]))
            return GetArray(name1, name2, (int[])def);
        return null;
    }
}

public class RegistryUtil
{
    public static T GetWithParent<T>(Registry reg, string name1, string name2, T def)
    {
        int id = reg.GetInt(name1, "ID", -1);
        while (id != -1)
        {
            foreach (Registry.RegistryNode node in reg.Root.Children)
            {
                int cid = reg.GetInt(node.Name, "ID", -1);
                if (cid == id)
                {
                    int parent_id = -1;
                    foreach (Registry.RegistryNode node2 in node.Children)
                    {
                        if (node2.Name.ToLower().Equals(name2.ToLower()))
                        {
                            if (typeof(T) == typeof(int) && node2.Type == Registry.RegistryNodeType.Int)
                                return (T)(object)node2.ValueI;
                            else if (typeof(T) == typeof(float) && node2.Type == Registry.RegistryNodeType.Float)
                                return (T)(object)node2.ValueF;
                            else if (typeof(T) == typeof(string) && node2.Type == Registry.RegistryNodeType.String)
                                return (T)(object)node2.ValueS;
                            else if (typeof(T) == typeof(int[]) && node2.Type == Registry.RegistryNodeType.Array)
                                return (T)(object)node2.ValueA;
                        }
                        else if (node2.Name == "Parent" && node2.Type == Registry.RegistryNodeType.Int)
                        {
                            parent_id = node2.ValueI;
                        }
                    }

                    // handle if not found
                    id = parent_id;
                    break;
                }
            }
        }

        return def;
    }
}
