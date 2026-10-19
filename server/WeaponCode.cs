using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LogServer;

/// <summary>
/// 게임 총기 공유 코드(WSC:…)를 해석한다. Unity 쪽 WeaponShareCode.Decode와 같은 형식이다.
/// 형식: "WSC:" + base64(헤더 1바이트 + 본문(deflate 가능) + HMAC-SHA256 앞 16바이트).
/// 본문: 이름 길이(가변 정수, 0이면 기본 이름) + 암호화 이름, 모듈 종류 수 + GUID 목록, 블록 수 + 블록 목록.
/// 블록: 모듈 번호, 셀 좌표(zigzag x·y·z), 플래그(출력 방향 3비트 | 법선 3비트 &lt;&lt; 3 | 색상 64), 색상이면 float 4개.
/// 이름은 복호화하지 않는다(집계에 쓰지 않고 개인 입력일 수 있다). 코어 블록은 코드에 들어가지 않는다.
/// </summary>
public static class WeaponCode
{
    private const string PREFIX = "WSC:";
    private const int MAX_CODE_LENGTH = 1000000;
    private const int MAX_BODY_BYTES = 1000000;
    private const int MAX_ITEMS = 1000;
    private const int MAC_BYTES = 16;

    private static readonly byte[] _macKey = SHA256.HashData(Encoding.UTF8.GetBytes("WGG.weapon-code.v1.compact-integrity"));

    /// <summary>
    /// 모듈 GUID(ModuleData.CodeId = 에셋 GUID) → 표시 이름. 게임 저장소 Assets/Data/Modules/*.asset의 codeId·displayName 기준이다.
    /// 모듈이 추가되면 여기에 더한다. 없는 GUID는 "모듈 " + 앞 8자리로 표시한다.
    /// </summary>
    private static readonly Dictionary<string, string> _moduleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["6289f0e60b334b04b823408ae1ae2e36"] = "기본 블럭",
        ["bf4a63288b8e9694787904964ba8acd4"] = "차지",
        ["d557014a75723774da9948844f570e45"] = "컬러",
        ["3819fc250847c48448b1dc45b4ce63a6"] = "폭발",
        ["bf1f0c3b718a48f9a19debdebc5da1a4"] = "연장",
        ["9b18b219018266c4a90db0f81b293ced"] = "연사",
        ["36f778d16d639104b90e807dc937aaaa"] = "중력",
        ["cf32abdb412c6124cabcbdd85c103cb5"] = "발열",
        ["0277d45126374004b802798d93561da2"] = "유도",
        ["d402a84ed473853498b702ccf20adc69"] = "관통",
        ["8185707d079aa3d4eba1d33833009eb2"] = "반사",
        ["cc30a6f07cbd432428e9db5a6f3315ae"] = "스코프",
        ["ac721da9683bfcc46b24fdba51a5b278"] = "방사",
        ["d5834196fd7a14146bee8605f3766655"] = "가속",
        ["9841ca931bf7a8c4b866da354b808ad7"] = "회전",
        ["4793f8d918e54829be0e1db07dc07830"] = "4방향 분할",
        ["be1aadfb48ae4f55ac16cb1d8d5429c8"] = "2방향 분할",
        ["5508fb3f89d14ebd9747c598276197c6"] = "트리거",
        ["8d0c0ddd9cf586745abb86717c7cc6b6"] = "물",
    };

    /// <summary>코어 구매가(게임 WeaponPurchasePrices.Core). 코드의 블록 목록에는 코어가 없다.</summary>
    public const int CorePrice = 50;

    /// <summary>
    /// 모듈 GUID → (달러 구매가 buyPrice, 모듈 재화 moduleCreditCost, 산탄 여부). 게임 Assets/Data/Modules/*.asset 기준이다.
    /// 산탄(방사)은 게임처럼 이미 단 산탄 수 + 1을 재화에 곱한다(WeaponPurchasePrices.ModuleCredits). 가격이 바뀌면 여기를 고친다.
    /// </summary>
    private static readonly Dictionary<string, (int Buy, int Credits, bool Shotgun)> _modulePrices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["6289f0e60b334b04b823408ae1ae2e36"] = (10, 0, false),   // 기본 블럭
        ["bf4a63288b8e9694787904964ba8acd4"] = (300, 50, false), // 차지
        ["d557014a75723774da9948844f570e45"] = (10, 0, false),   // 컬러
        ["3819fc250847c48448b1dc45b4ce63a6"] = (500, 0, false),  // 폭발
        ["bf1f0c3b718a48f9a19debdebc5da1a4"] = (100, 0, false),  // 연장
        ["9b18b219018266c4a90db0f81b293ced"] = (100, 0, false),  // 연사
        ["36f778d16d639104b90e807dc937aaaa"] = (500, 200, false), // 중력
        ["cf32abdb412c6124cabcbdd85c103cb5"] = (500, 200, false), // 발열
        ["0277d45126374004b802798d93561da2"] = (500, 100, false), // 유도
        ["d402a84ed473853498b702ccf20adc69"] = (500, 0, false),  // 관통
        ["8185707d079aa3d4eba1d33833009eb2"] = (300, 50, false), // 반사
        ["cc30a6f07cbd432428e9db5a6f3315ae"] = (100, 0, false),  // 스코프
        ["ac721da9683bfcc46b24fdba51a5b278"] = (300, 50, true),  // 방사(산탄)
        ["d5834196fd7a14146bee8605f3766655"] = (100, 0, false),  // 가속
        ["9841ca931bf7a8c4b866da354b808ad7"] = (200, 100, false), // 회전
        ["4793f8d918e54829be0e1db07dc07830"] = (400, 0, false),  // 4방향 분할
        ["be1aadfb48ae4f55ac16cb1d8d5429c8"] = (200, 0, false),  // 2방향 분할
        ["5508fb3f89d14ebd9747c598276197c6"] = (100, 0, false),  // 트리거
        ["8d0c0ddd9cf586745abb86717c7cc6b6"] = (50, 10, false),  // 물
    };

    /// <summary>모듈 구매가·재화. 표에 없는 모듈은 게임 기본 모듈가(WeaponPurchasePrices.Module = 30)와 재화 0으로 본다.</summary>
    internal static (int Buy, int Credits, bool Shotgun) ModulePrice(string guid) =>
        _modulePrices.TryGetValue(guid, out var price) ? price : (30, 0, false);

    /// <summary>모듈 GUID를 표시 이름으로 바꾼다.</summary>
    public static string ModuleName(string guid) =>
        _moduleNames.TryGetValue(guid, out string? name) ? name : "모듈 " + guid[..Math.Min(8, guid.Length)];

    /// <summary>
    /// 코드를 해석한다. 형식·무결성·범위 검사 중 하나라도 틀리면 null을 반환한다(예외를 던지지 않는다).
    /// </summary>
    public static WeaponLayout? TryDecode(string? code)
    {
        try
        {
            return Decode(code);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or EndOfStreamException
                                       or ArgumentException or IOException or OverflowException)
        {
            return null;
        }
    }

    private static WeaponLayout? Decode(string? code)
    {
        code = (code ?? string.Empty).Trim();
        if (!code.StartsWith(PREFIX, StringComparison.Ordinal) || code.Length > MAX_CODE_LENGTH)
        {
            return null;
        }

        string encoded = code[PREFIX.Length..];
        byte[] bytes = Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '='));
        if (bytes.Length < MAC_BYTES + 4)
        {
            return null;
        }

        ReadOnlySpan<byte> payload = bytes.AsSpan(0, bytes.Length - MAC_BYTES);
        byte[] expected = HMACSHA256.HashData(_macKey, payload);
        if (!CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, MAC_BYTES), bytes.AsSpan(payload.Length)))
        {
            return null;
        }

        if ((payload[0] & 127) != 2)
        {
            return null;
        }

        byte[] body = payload[1..].ToArray();
        if ((payload[0] & 128) != 0)
        {
            body = Inflate(body);
        }

        using MemoryStream stream = new(body);
        using BinaryReader reader = new(stream);
        uint nameLength = ReadNumber(reader);
        if (nameLength > 256 || nameLength % 16 != 0)
        {
            return null;
        }

        Exact(reader, (int)nameLength);

        uint moduleCount = ReadNumber(reader);
        if (moduleCount > MAX_ITEMS)
        {
            return null;
        }

        string[] modules = new string[moduleCount];
        for (int i = 0; i < modules.Length; i++)
        {
            modules[i] = new Guid(Exact(reader, 16)).ToString("N");
        }

        uint blockCount = ReadNumber(reader);
        if (blockCount > MAX_ITEMS)
        {
            return null;
        }

        List<WeaponBlock> blocks = new((int)blockCount);
        HashSet<(int, int, int)> cells = new() { (0, 0, 0) };
        for (int i = 0; i < blockCount; i++)
        {
            uint id = ReadNumber(reader);
            if (id >= moduleCount)
            {
                return null;
            }

            (int x, int y, int z) cell = (ReadCoordinate(reader), ReadCoordinate(reader), ReadCoordinate(reader));
            byte flags = reader.ReadByte();
            int output = flags & 7, normal = (flags >> 3) & 7;
            if ((flags & 128) != 0 || output >= 6 || normal >= 6 || !cells.Add(cell))
            {
                return null;
            }

            bool hasColor = (flags & 64) != 0;
            if (hasColor)
            {
                Exact(reader, 16);
            }

            blocks.Add(new WeaponBlock(modules[id], cell.x, cell.y, cell.z, output, normal, hasColor));
        }

        if (stream.Position != stream.Length)
        {
            return null;
        }

        return new WeaponLayout(nameLength > 0, blocks);
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using MemoryStream input = new(compressed);
        using DeflateStream inflater = new(input, CompressionMode.Decompress);
        using MemoryStream output = new();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = inflater.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + count > MAX_BODY_BYTES)
            {
                throw new InvalidDataException("weapon body too large");
            }

            output.Write(buffer, 0, count);
        }

        return output.ToArray();
    }

    /// <summary>7비트 가변 길이 부호 없는 정수(최대 5바이트).</summary>
    private static uint ReadNumber(BinaryReader reader)
    {
        uint n = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            byte b = reader.ReadByte();
            if (shift == 28 && (b & 240) != 0)
            {
                throw new InvalidDataException("bad number");
            }

            n |= (uint)(b & 127) << shift;
            if ((b & 128) == 0)
            {
                return n;
            }
        }

        throw new InvalidDataException("bad number");
    }

    private static int ReadCoordinate(BinaryReader reader)
    {
        uint n = ReadNumber(reader);
        return unchecked((int)(n >> 1) ^ -(int)(n & 1));
    }

    private static byte[] Exact(BinaryReader reader, int count)
    {
        byte[] bytes = reader.ReadBytes(count);
        if (bytes.Length != count)
        {
            throw new EndOfStreamException("weapon code truncated");
        }

        return bytes;
    }
}

/// <summary>모듈 블록 하나. Module은 GUID(N 형식), 방향은 +X,-X,+Y,-Y,+Z,-Z 순 인덱스다.</summary>
public readonly record struct WeaponBlock(string Module, int X, int Y, int Z, int Output, int Normal, bool HasColor);

/// <summary>해석한 총기 배치. CustomName은 기본 이름이 아니면 true(이름 자체는 읽지 않는다).</summary>
public sealed class WeaponLayout
{
    public WeaponLayout(bool customName, List<WeaponBlock> blocks)
    {
        CustomName = customName;
        Blocks = blocks;
        Key = ComputeKey(blocks);
    }

    public bool CustomName { get; }

    public List<WeaponBlock> Blocks { get; }

    /// <summary>
    /// 구성 식별자. 게임 WeaponShareCode.GetAssemblyKey(= EquipableWeapon.TestAssemblyKey, 로그의 weaponAssemblyId)와 같은 값이다.
    /// 모듈·셀·출력 방향·법선만으로 만들며 이름·색상은 제외한다.
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// 튜토리얼(Day0)이 만들어 주는 기본 구성인지. 코어의 오른쪽·왼쪽·위·아래·뒤 다섯 면에 기본 블럭을
    /// 법선 = 그 면으로 붙인 총이다(TutorialManager.Flow prepareBlockedCore). 실제 로그에서 출력 방향은 모두 +Z라 보지 않는다.
    /// </summary>
    public bool IsTutorialDefault =>
        Blocks.Count == 5 && Blocks.All(b =>
            WeaponCode.ModuleName(b.Module) == "기본 블럭" && TutorialFaces.Contains(b.Normal)
            && (b.X, b.Y, b.Z) == Directions[b.Normal])
        && Blocks.Select(b => b.Normal).Distinct().Count() == 5;

    /// <summary>방향 인덱스 순서: +X, -X, +Y, -Y, +Z, -Z (게임 Directions와 같음).</summary>
    internal static readonly (int X, int Y, int Z)[] Directions = { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };

    private static readonly int[] TutorialFaces = { 0, 1, 2, 3, 5 };

    /// <summary>모듈 표시 이름별 블록 개수.</summary>
    /// <summary>
    /// 총기 가치: 달러 = 코어 + 모듈 구매가 합, 재화 = 모듈 재화 합(산탄은 단 순서대로 재화 × 1, 2, 3…).
    /// 게임이 공유 코드로 같은 총을 다시 만들 때 받는 비용과 같은 규칙이다.
    /// </summary>
    public (int Dollars, int Credits) Value()
    {
        int dollars = WeaponCode.CorePrice, credits = 0, shotguns = 0;
        foreach (WeaponBlock block in Blocks)
        {
            (int buy, int cost, bool shotgun) = WeaponCode.ModulePrice(block.Module);
            dollars += buy;
            credits += shotgun ? cost * ++shotguns : cost;
        }

        return (dollars, credits);
    }

    public Dictionary<string, int> PartCounts()
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (WeaponBlock block in Blocks)
        {
            string name = WeaponCode.ModuleName(block.Module);
            counts.TryGetValue(name, out int count);
            counts[name] = count + 1;
        }

        return counts;
    }

    /// <summary>
    /// 게임 GetAssemblyKey 재현: BinaryWriter로 int 1, 그리고 셀 x·y·z 순 정렬된 블록마다
    /// 모듈 GUID 문자열(길이 접두 UTF-8), 셀·출력 방향·법선 벡터의 x·y·z(int)를 쓴 바이트의 SHA-256 base64.
    /// </summary>
    private static string ComputeKey(List<WeaponBlock> blocks)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(1);
            foreach (WeaponBlock b in blocks.OrderBy(b => b.X).ThenBy(b => b.Y).ThenBy(b => b.Z))
            {
                writer.Write(b.Module);
                (int X, int Y, int Z) output = Directions[b.Output], normal = Directions[b.Normal];
                foreach ((int x, int y, int z) in new[] { (b.X, b.Y, b.Z), output, normal })
                {
                    writer.Write(x);
                    writer.Write(y);
                    writer.Write(z);
                }
            }
        }

        return Convert.ToBase64String(SHA256.HashData(stream.ToArray()));
    }
}
