// bcrypt-unsafe.cs — BCrypt AES-GCM 解密验证工具（unsafe fixed 写法，复刻 dotnet/runtime AeadCommon.Windows.cs）
// 用途：验证 zcode 凭证 enc:v1 的解密，并记录一个关键坑：
//   BCRYPT_AUTHENTICATED_CIPHER_MODE_INFO 的真实内存布局是 cbSize 在前、dwInfoVersion 在后，
//   与 MSDN 文档页写的顺序（dwInfoVersion 在前）相反——按文档写会得到 0xC000000D。
//   另外结构体末尾还有 cbAAD(int) 与 cbData(long) 两个字段，文档页同样没写全，漏掉也会 0xC000000D。
// 用法: bcrypt-unsafe.exe <credential-secret>  （密文放在 tools\enc-blob.txt，格式 enc:v1:iv.tag.ct base64url）
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

unsafe class T
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AUTH
    {
        public int cbSize; public uint v;
        public byte* nonce; public int cbNonce;
        public byte* aad; public int cbAad;
        public byte* tag; public int cbTag;
        public byte* mac; public int cbMac;
        public int cbAAD; public long cbData;
        public uint flags;
        public static AUTH Create() { AUTH a = default(AUTH); a.cbSize = sizeof(AUTH); a.v = 1; return a; }
    }
    [DllImport("bcrypt.dll", EntryPoint = "BCryptOpenAlgorithmProvider", CharSet = CharSet.Unicode)] public static extern int Open(out IntPtr h, string alg, string impl, uint f);
    [DllImport("bcrypt.dll", EntryPoint = "BCryptSetProperty", CharSet = CharSet.Unicode)] public static extern int SetProp(IntPtr o, string p, byte[] v, int size, uint f);
    [DllImport("bcrypt.dll", EntryPoint = "BCryptGenerateSymmetricKey")] public static extern int GenKey(IntPtr alg, out IntPtr k, IntPtr obj, int cbObj, byte[] secret, int cb, uint f);
    [DllImport("bcrypt.dll", EntryPoint = "BCryptDestroyKey")] public static extern int DestroyKey(IntPtr k);
    [DllImport("bcrypt.dll", EntryPoint = "BCryptCloseAlgorithmProvider")] public static extern int CloseAlg(IntPtr h, uint f);
    [DllImport("bcrypt.dll", EntryPoint = "BCryptDecrypt")] public static extern int Dec(IntPtr k, byte* inp, int cbIn, IntPtr pad, byte[] iv, int cbIv, byte* outp, int cbOut, out int res, int f);

    static void Main()
    {
        string[] p = File.ReadAllLines(@"tools\enc-blob.txt")[0].Trim().Split('.');
        byte[] iv = B64(p[0].Substring(7)), tag = B64(p[1]), ct = B64(p[2]);
        byte[] key = SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Environment.GetCommandLineArgs()[1]));   // 密钥串经参数传入，勿写死机器信息

        IntPtr hAlg, hKey;
        if (Open(out hAlg, "AES", null, 0) != 0) { Console.WriteLine("open fail"); return; }
        byte[] gcm = Encoding.Unicode.GetBytes("ChainingModeGCM\0");
        Console.WriteLine("setprop=" + SetProp(hAlg, "ChainingMode", gcm, gcm.Length, 0));
        Console.WriteLine("genkey=" + GenKey(hAlg, out hKey, IntPtr.Zero, 0, key, key.Length, 0));
        Console.WriteLine("sizeof(AUTH)=" + sizeof(AUTH));

        fixed (byte* pNonce = iv, pTag = tag, pIn = ct, pOut = new byte[ct.Length])
        {
            AUTH a = AUTH.Create();
            a.nonce = pNonce; a.cbNonce = iv.Length;
            a.tag = pTag; a.cbTag = tag.Length;
            byte[] ob = new byte[ct.Length];
            int got;
            fixed (byte* po = ob)
            {
                int st = Dec(hKey, pIn, ct.Length, new IntPtr(&a), null, 0, po, ob.Length, out got, 0);
                Console.WriteLine("decrypt status=0x{0:X} got={1}", st, got);
                if (st == 0) Console.WriteLine("head: " + Encoding.UTF8.GetString(ob, 0, Math.Min(24, got)));
            }
        }
        DestroyKey(hKey); CloseAlg(hAlg, 0);
    }

    static byte[] B64(string s)
    {
        string b = s.Replace('-', '+').Replace('_', '/');
        while (b.Length % 4 != 0) b += "=";
        return Convert.FromBase64String(b);
    }
}
