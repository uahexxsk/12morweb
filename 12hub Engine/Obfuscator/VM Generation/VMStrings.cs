namespace Hub12.Obfuscator.VM_Generation
{
	public static class VMStrings
	{
		public static string VMP1 = @"
local BitXOR = bit and bit.bxor or function(a,b)
    local p,c=1,0
    while a>0 and b>0 do
        local ra,rb=a%2,b%2
        if ra~=rb then c=c+p end
        a,b,p=(a-ra)/2,(b-rb)/2,p*2
    end
    if a<b then a=b end
    while a>0 do
        local ra=a%2
        if ra>0 then c=c+p end
        a,p=(a-ra)/2,p*2
    end
    return c
end

local function gBit(Bit, Start, End)
	if End then
		local Res = (Bit / 2 ^ (Start - 1)) % 2 ^ ((End - 1) - (Start - 1) + 1);

		return Res - Res % 1;
	else
		local Plc = 2 ^ (Start - 1);

        return (Bit % (Plc + Plc) >= Plc) and 1 or 0;
	end;
end;

local Pos = 1;
local RollingXorKey = XOR_KEY;
local TypeOf = type;
local Raise = error;

-- Fail-fast integrity and bounds guards. These checks report corruption instead of
-- hanging the host process or reading outside the serialized payload.
local function NeedBytes(Count)
    if TypeOf(Count) ~= ""number"" or Count < 0 or Count % 1 ~= 0 or Pos + Count - 1 > #ByteString then
        Raise(""12hub: VM payload bounds check failed"", 0);
    end;
end;

local function CheckPayloadIntegrity()
    if TypeOf(ByteString) ~= ""string"" then return false end;
    local Checksum = INTEGRITY_SEED;
    local Checksum2 = INTEGRITY_SEED_2;
    for Index = 1, #ByteString do
        local Value = Byte(ByteString, Index);
        Checksum = (Checksum * INTEGRITY_MULTIPLIER + Value) % 4294967291;
        Checksum2 = (Checksum2 * INTEGRITY_MULTIPLIER_2 + Value) % 4294967291;
    end;
    return Checksum == PAYLOAD_HASH and Checksum2 == PAYLOAD_HASH_2;
end;
if not CheckPayloadIntegrity() then
    Raise(""12hub: VM payload integrity check failed"", 0);
end;

-- This state is shared by every byte/word/string read, matching the C# writer.
local function DecodeByte(Value)
    local Decoded = BitXOR(Value, RollingXorKey);
    RollingXorKey = Decoded % 256;
    return Decoded;
end;

local function gBits32()
    NeedBytes(4);
    local W, X, Y, Z = Byte(ByteString, Pos, Pos + 3);

    W = DecodeByte(W);
    X = DecodeByte(X);
    Y = DecodeByte(Y);
    Z = DecodeByte(Z);

    Pos = Pos + 4;
    return (Z*16777216) + (Y*65536) + (X*256) + W;
end;

local function gBits8()
    NeedBytes(1);
    local F = DecodeByte(Byte(ByteString, Pos, Pos));
    Pos = Pos + 1;
    return F;
end;

local function gFloat()
	local Left = gBits32();
	local Right = gBits32();
	local IsNormal = 1;
	local Mantissa = (gBit(Right, 1, 20) * (2 ^ 32))
					+ Left;
	local Exponent = gBit(Right, 21, 31);
	local Sign = ((-1) ^ gBit(Right, 32));
	if (Exponent == 0) then
		if (Mantissa == 0) then
			return Sign * 0; -- +-0
		else
			Exponent = 1;
			IsNormal = 0;
		end;
	elseif (Exponent == 2047) then
        return (Mantissa == 0) and (Sign * (1 / 0)) or (Sign * (0 / 0));
	end;
	return LDExp(Sign, Exponent - 1023) * (IsNormal + (Mantissa / (2 ^ 52)));
end;

local gSizet = gBits32;
local function gString(Len)
    local Str;
    if (not Len) then
        Len = gSizet();
        if (Len == 0) then
            return '';
        end;
    end;

    if TypeOf(Len) ~= ""number"" or Len < 0 or Len % 1 ~= 0 then
        Raise(""12hub: invalid VM string length"", 0);
    end;
    NeedBytes(Len);
    Str = Sub(ByteString, Pos, Pos + Len - 1);
    Pos = Pos + Len;

    local FStr = {}
    for Idx = 1, #Str do
        FStr[Idx] = Char(DecodeByte(Byte(Sub(Str, Idx, Idx))))
    end

    return Concat(FStr);
end;

local gInt = gBits32;
local function _R(...) return {...}, Select('#', ...) end

local function Deserialize()
    local Instrs = { INSTR_CNT };
    local Functions = { FUNC_CNT };
	local Lines = {};
    local Chunk = 
	{
		Instrs,
		nil,
		Functions,
		nil,
		Lines
	};";
		
		public static string VMP2 = @"
local function Wrap(Chunk, Upvalues, Env)
	local Instr  = Chunk[1];
	local Const  = Chunk[2];
	local Proto  = Chunk[3];
	local Params = Chunk[4];

	return function(...)
		local Instr  = Instr; 
		local Const  = Const; 
		local Proto  = Proto; 
		local Params = Params;

		local _R = _R
		local InstrPoint = 1;
		local Top = -1;

		local Vararg = {};
		local Args	= {...};

		local PCount = Select('#', ...) - 1;

		local Lupvals	= {};
		local Stk		= {};

		for Idx = 0, PCount do
			if (Idx >= Params) then
				Vararg[Idx - Params] = Args[Idx + 1];
			else
				Stk[Idx] = Args[Idx + 1];
			end;
		end;

		local Varargsz = PCount - Params + 1

		local Inst;
		local Enum;	

		while true do
			Inst = Instr[InstrPoint];
            if TypeOf(Inst) ~= ""table"" then Raise(""12hub: VM instruction integrity check failed"", 0); end;
			Enum = Inst[OP_ENUM];
            if TypeOf(Enum) ~= ""number"" or Enum < 0 or Enum >= OPCODE_COUNT then
                Raise(""12hub: VM opcode integrity check failed"", 0);
            end;";

		public static string VMP3 = @"
			InstrPoint	= InstrPoint + 1;
		end;
    end;
end;	
return Wrap(Deserialize(), {}, GetFEnv())();
";
		public static string VMP2_LI = @"
local PCall = pcall
local function Wrap(Chunk, Upvalues, Env)
	local Instr = Chunk[1];
	local Const = Chunk[2];
	local Proto = Chunk[3];
	local Params = Chunk[4];

	return function(...)
		local InstrPoint = 1;
		local Top = -1;

		local Args = {...};
		local PCount = Select('#', ...) - 1;

		local function Loop()
			local Instr  = Instr; 
			local Const  = Const; 
			local Proto  = Proto; 
			local Params = Params;

			local _R = _R
			local Vararg = {};

			local Lupvals	= {};
			local Stk		= {};
	
			for Idx = 0, PCount do
				if (Idx >= Params) then
					Vararg[Idx - Params] = Args[Idx + 1];
				else
					Stk[Idx] = Args[Idx + 1];
				end;
			end;
	
			local Varargsz = PCount - Params + 1

			local Inst;
			local Enum;	

			while true do
				Inst = Instr[InstrPoint];
                if TypeOf(Inst) ~= ""table"" then Raise(""12hub: VM instruction integrity check failed"", 0); end;
				Enum = Inst[OP_ENUM];
                if TypeOf(Enum) ~= ""number"" or Enum < 0 or Enum >= OPCODE_COUNT then
                    Raise(""12hub: VM opcode integrity check failed"", 0);
                end;";
		
		public static string VMP3_LI = @"
				InstrPoint	= InstrPoint + 1;
			end;
		end;

		A, B = _R(PCall(Loop))
		if not A[1] then
			local line = Chunk[7][InstrPoint] or '?'
			error('ERROR IN 12HUB SCRIPT [LINE ' .. line .. ']:' .. A[2])
		else
			return Unpack(A, 2, B)
		end;
	end;
end;	
return Wrap(Deserialize(), {}, GetFEnv())();
";
	}
}