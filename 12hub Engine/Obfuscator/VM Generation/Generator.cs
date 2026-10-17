using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;
using Hub12.Extensions;
using Hub12.Obfuscator.Opcodes;
using Hub12.Obfuscator.Encryption;

namespace Hub12.Obfuscator.VM_Generation
{
	public class Generator
	{
		private ObfuscationContext _context;
		
		public Generator(ObfuscationContext context) =>
			_context = context;

		public bool IsUsed(Chunk chunk, VOpcode virt)
		{
			bool isUsed = false;
			foreach (Instruction ins in chunk.Instructions)
				if (virt.IsInstruction(ins))
				{
					if (!_context.InstructionMapping.ContainsKey(ins.OpCode))
						_context.InstructionMapping.Add(ins.OpCode, virt);

					ins.CustomData = new CustomInstructionData {Opcode = virt};
					isUsed = true;
				}

			foreach (Chunk sChunk in chunk.Functions)
				isUsed |= IsUsed(sChunk, virt);

			return isUsed;
		}

		public static List<int> Compress(byte[] uncompressed)
		{
			// build the dictionary
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			for (int i = 0; i < 256; i++)
				dictionary.Add(((char)i).ToString(), i);
 
			string    w          = string.Empty;
			List<int> compressed = new List<int>();
 
			foreach (byte b in uncompressed)
			{
				string wc = w + (char)b;
				if (dictionary.ContainsKey(wc))
					w = wc;
				
				else
				{
					// write w to output
					compressed.Add(dictionary[w]);
					// wc is a new sequence; add it to the dictionary
					dictionary.Add(wc, dictionary.Count);
					w = ((char) b).ToString();
				}
			}
 
			// write remaining output if necessary
			if (!string.IsNullOrEmpty(w))
				compressed.Add(dictionary[w]);
 
			return compressed;
		}

        private static string CreatePayloadAlphabet()
        {
            const string pool = "!\"#$%&()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[]^_`abcdefghijklmnopqrstuvwxyz{|}~";
            char[] chars = pool.ToCharArray();
            Random random = Hub12.TwelveHubEngine.Random;
            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = random.Next(0, i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }
            return new string(chars);
        }

        private static string EncodeWithAlphabet(ulong value, string alphabet)
        {
            if (string.IsNullOrEmpty(alphabet) || alphabet.Length < 2)
                throw new ArgumentException("A payload alphabet with at least two symbols is required.", nameof(alphabet));

            StringBuilder sb = new StringBuilder(13);
            do
            {
                sb.Insert(0, alphabet[(int)(value % (ulong)alphabet.Length)]);
                value /= (ulong)alphabet.Length;
            } while (value != 0);
            return sb.ToString();
        }

        public static string CompressedToString(List<int> compressed, string alphabet)
        {
            if (alphabet == null || alphabet.Length < 2)
                throw new ArgumentException("A payload alphabet is required.", nameof(alphabet));

            StringBuilder sb = new StringBuilder();
            foreach (int i in compressed)
            {
                string n = EncodeWithAlphabet((ulong)i, alphabet);
                if (n.Length >= alphabet.Length)
                    throw new InvalidOperationException("Compressed payload length prefix exceeds alphabet capacity.");
                sb.Append(alphabet[n.Length]);
                sb.Append(n);
            }

            return sb.ToString();
        }

		public List<OpMutated> GenerateMutations(List<VOpcode> opcodes)
		{
			Random r = Hub12.TwelveHubEngine.Random;
			List<OpMutated> mutated = new List<OpMutated>();

			foreach (VOpcode opc in opcodes)
			{
				if (opc is OpSuperOperator)
					continue;

				for (int i = 0; i < r.Next(35, 50); i++)
				{
					int[] rand = {0, 1, 2};
					rand.Shuffle();

					OpMutated mut = new OpMutated();

					mut.Registers = rand;
					mut.Mutated = opc;
						
					mutated.Add(mut);
				}
			}

			mutated.Shuffle();
			return mutated;
		}

		public void FoldMutations(List<OpMutated> mutations, HashSet<OpMutated> used, Chunk chunk)
		{
			bool[] skip = new bool[chunk.Instructions.Count + 1];
			
			for (int i = 0; i < chunk.Instructions.Count; i++)
			{
				Instruction opc = chunk.Instructions[i];

				switch (opc.OpCode)
				{
					case Opcode.Closure:
						for (int j = 1; j <= ((Chunk) opc.RefOperands[0]).UpvalueCount; j++)
							skip[i + j] = true;

						break;
				}
			}
			
			for (int i = 0; i < chunk.Instructions.Count; i++)
			{
				if (skip[i])
					continue;
				
				Instruction opc = chunk.Instructions[i];
				CustomInstructionData data = opc.CustomData;
				
				foreach (OpMutated mut in mutations)
					if (data.Opcode == mut.Mutated && data.WrittenOpcode == null)
					{
						if (!used.Contains(mut))
							used.Add(mut);

						data.Opcode = mut;
						break;
					}
			}
			
			foreach (Chunk _c in chunk.Functions)
				FoldMutations(mutations, used, _c);
		}

		public List<OpSuperOperator> GenerateSuperOperators(Chunk chunk, int maxSize, int minSize = 5)
		{
			List<OpSuperOperator> results = new List<OpSuperOperator>();
			Random                r       = Hub12.TwelveHubEngine.Random;

			bool[] skip = new bool[chunk.Instructions.Count + 1];

			for (int i = 0; i < chunk.Instructions.Count - 1; i++)
			{
				switch (chunk.Instructions[i].OpCode)
				{
					case Opcode.Closure:
					{
						skip[i] = true;
						for (int j = 0; j < ((Chunk) chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
							skip[i + j + 1] = true;
							
						break;
					}

					case Opcode.Eq:
					case Opcode.Lt:
					case Opcode.Le:
					case Opcode.Test:
					case Opcode.TestSet:
					case Opcode.TForLoop:
					case Opcode.SetList:
					case Opcode.LoadBool when chunk.Instructions[i].C != 0:
						skip[i + 1] = true;
						break;

					case Opcode.ForLoop:
					case Opcode.ForPrep:
					case Opcode.Jmp:
						chunk.Instructions[i].UpdateRegisters();
						
						skip[i + 1] = true;
						skip[i + chunk.Instructions[i].B + 1] = true;
						break;
				}
				
				if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
					for (int j = 0; j < su.SubOpcodes.Length; j++)
						skip[i + j] = true;
			}
			
			int c = 0;
			while (c < chunk.Instructions.Count)
			{
				int targetCount = maxSize;
				OpSuperOperator superOperator = new OpSuperOperator {SubOpcodes = new VOpcode[targetCount]};

				bool d     = true;
				int cutoff = targetCount;

				for (int j = 0; j < targetCount; j++)
					if (c + j > chunk.Instructions.Count - 1 || skip[c + j])
					{
						cutoff = j; 
						d = false;
						break;
					}

				if (!d)
				{
					if (cutoff < minSize)
					{
						c += cutoff + 1;	
						continue;
					}
						
					targetCount = cutoff;	
					superOperator = new OpSuperOperator {SubOpcodes = new VOpcode[targetCount]};
				}
				
				for (int j = 0; j < targetCount; j++)
					superOperator.SubOpcodes[j] =
						chunk.Instructions[c + j].CustomData.Opcode;

				results.Add(superOperator);
				c += targetCount + 1;
			}

			foreach (var _c in chunk.Functions)
				results.AddRange(GenerateSuperOperators(_c, maxSize));
			
			return results;
		}

		public void FoldAdditionalSuperOperators(Chunk chunk, List<OpSuperOperator> operators, ref int folded)
		{
			bool[] skip = new bool[chunk.Instructions.Count + 1];
			for (int i = 0; i < chunk.Instructions.Count - 1; i++)
			{
				switch (chunk.Instructions[i].OpCode)
				{
					case Opcode.Closure:
					{
						skip[i] = true;
						for (int j = 0; j < ((Chunk) chunk.Instructions[i].RefOperands[0]).UpvalueCount; j++)
							skip[i + j + 1] = true;
							
						break;
					}

					case Opcode.Eq:
					case Opcode.Lt:
					case Opcode.Le:
					case Opcode.Test:
					case Opcode.TestSet:
					case Opcode.TForLoop:
					case Opcode.SetList:
					case Opcode.LoadBool when chunk.Instructions[i].C != 0:
						skip[i + 1] = true;
						break;

					case Opcode.ForLoop:
					case Opcode.ForPrep:
					case Opcode.Jmp:
						chunk.Instructions[i].UpdateRegisters();
						skip[i + 1] = true;
						skip[i + chunk.Instructions[i].B + 1] = true;
						break;
				}
				
				if (chunk.Instructions[i].CustomData.WrittenOpcode is OpSuperOperator su && su.SubOpcodes != null)
					for (int j = 0; j < su.SubOpcodes.Length; j++)
						skip[i + j] = true;
			}
			
			int c = 0;
			while (c < chunk.Instructions.Count)
			{
				if (skip[c])
				{
					c++;
					continue;
				}

				bool used = false;

				foreach (OpSuperOperator op in operators)
				{
					int targetCount = op.SubOpcodes.Length;
					bool cu = true;
					for (int j = 0; j < targetCount; j++)
					{
						if (c + j > chunk.Instructions.Count - 1 || skip[c + j])
						{
							cu = false;
							break;
						}
					}

					if (!cu)
						continue;


					List<Instruction> taken = chunk.Instructions.Skip(c).Take(targetCount).ToList();
					if (op.IsInstruction(taken))
					{
						for (int j = 0; j < targetCount; j++)
						{
							skip[c + j] = true;
							chunk.Instructions[c + j].CustomData.WrittenOpcode = new OpSuperOperator {VIndex = 0};
						}

						chunk.Instructions[c].CustomData.WrittenOpcode = op;

						used = true;
						break;
					}
				}

				if (!used)
					c++;
				else
					folded++;
			}

			foreach (var _c in chunk.Functions)
				FoldAdditionalSuperOperators(_c, operators, ref folded);
		}
		
		public string GenerateVM(ObfuscationSettings settings)
		{
			Random r = Hub12.TwelveHubEngine.Random;

			List<VOpcode> virtuals = Assembly.GetExecutingAssembly().GetTypes()
			                                 .Where(t => t.IsSubclassOf(typeof(VOpcode)))
			                                 .Select(Activator.CreateInstance)
			                                 .Cast<VOpcode>()
			                                 .Where(t => IsUsed(_context.HeadChunk, t))
			                                 .ToList();

			
			if (settings.Mutate)
			{
				List<OpMutated> muts = GenerateMutations(virtuals).Take(settings.MaxMutations).ToList();
				
				Console.WriteLine("Created " + muts.Count + " mutations.");
				
				HashSet<OpMutated> used = new HashSet<OpMutated>();
				FoldMutations(muts, used, _context.HeadChunk);
				
				Console.WriteLine("Used " + used.Count + " mutations.");
				
				virtuals.AddRange(used);
			}
			
			if (settings.SuperOperators)
			{
				int folded = 0;
				
				var megaOperators = GenerateSuperOperators(_context.HeadChunk, 80, 60).OrderBy(t => r.Next())
					.Take(settings.MaxMegaSuperOperators).ToList();
				
				Console.WriteLine("Created " + megaOperators.Count + " mega super operators.");
				
				virtuals.AddRange(megaOperators);
				
				FoldAdditionalSuperOperators(_context.HeadChunk, megaOperators, ref folded);
				
				var miniOperators = GenerateSuperOperators(_context.HeadChunk, 10).OrderBy(t => r.Next())
					.Take(settings.MaxMiniSuperOperators).ToList();
				
				Console.WriteLine("Created " + miniOperators.Count + " mini super operators.");
				
				virtuals.AddRange(miniOperators);
				
				FoldAdditionalSuperOperators(_context.HeadChunk, miniOperators, ref folded);
				
				Console.WriteLine("Folded " + folded + " instructions into super operators.");
			}
			
			virtuals.Shuffle();
			
			for (int i = 0; i < virtuals.Count; i++)
				virtuals[i].VIndex = i;

			string vm = "";

			byte[] bs = new Serializer(_context, settings).SerializeLChunk(_context.HeadChunk);
			
			vm += @"
local Byte         = string.byte;
local Char         = string.char;
local Sub          = string.sub;
local Concat       = table.concat;
local LDExp        = math.ldexp;
local GetFEnv      = getfenv or function() return _ENV end;
local Setmetatable = setmetatable;
local Select       = select;

local Unpack = unpack;
local ToNumber = tonumber;";

			if (settings.BytecodeCompress)
			{
				string payloadAlphabet = CreatePayloadAlphabet();
				string packedPayload = CompressedToString(Compress(bs), payloadAlphabet);
				int packedIntegritySeed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
				int packedIntegrityMultiplier = RandomNumberGenerator.GetInt32(37, 252);
				int packedIntegritySeed2 = RandomNumberGenerator.GetInt32(1, int.MaxValue);
				int packedIntegrityMultiplier2 = RandomNumberGenerator.GetInt32(13, 252);
				if (packedIntegrityMultiplier2 == packedIntegrityMultiplier)
					packedIntegrityMultiplier2 = packedIntegrityMultiplier2 == 251 ? 13 : packedIntegrityMultiplier2 + 1;
				byte[] packedIntegrityBytes = Encoding.ASCII.GetBytes(packedPayload + payloadAlphabet);
				long packedIntegrityHash = VMIntegrityCheck.ComputePayloadHash(
					packedIntegrityBytes, packedIntegritySeed, packedIntegrityMultiplier);
				long packedIntegrityHash2 = VMIntegrityCheck.ComputePayloadHash(
					packedIntegrityBytes, packedIntegritySeed2, packedIntegrityMultiplier2);

				// Verify the encoded data and alphabet before the decompressor consumes it.
				// The decoded bytecode is checked independently by VMStrings.VMP1 below.
				vm += @"local PackedType=type; local PackedRaise=error;
local function CheckPackedPayload(P,A,L,S1,M1,E1,S2,M2,E2)
    if PackedType(P)~='string' or PackedType(A)~='string' or #P+#A~=L then return false end;
    local H1=S1; local H2=S2;
    for I=1,#P do local V=Byte(P,I); H1=(H1*M1+V)%4294967291; H2=(H2*M2+V)%4294967291 end;
    for I=1,#A do local V=Byte(A,I); H1=(H1*M1+V)%4294967291; H2=(H2*M2+V)%4294967291 end;
    return H1==E1 and H2==E2;
end;
";
				vm += "local function decompress(b,a)local c,d,e=\"\",\"\",{}local f=256;local g={}for h=0,f-1 do g[h]=Char(h)end;local q=1;local function k()local l=string.find(a,Sub(b,q,q),1,true)-1;q=q+1;local v=0;for j=q,q+l-1 do v=v*" + payloadAlphabet.Length + "+(string.find(a,Sub(b,j,j),1,true)-1)end;q=q+l;return v end;c=Char(k())e[1]=c;while q<=#b do local n=k()if g[n]then d=g[n]else d=c..Sub(c,1,1)end;g[f]=c..Sub(d,1,1)e[#e+1],c,f=d,d,f+1 end;return table.concat(e)end;";
				vm += "local PackedPayload='" + packedPayload + "'; local PayloadAlphabet='" + payloadAlphabet + "';\n";
				vm += "if not CheckPackedPayload(PackedPayload,PayloadAlphabet," + (packedPayload.Length + payloadAlphabet.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegritySeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegrityMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegrityHash.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegritySeed2.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegrityMultiplier2.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ "," + packedIntegrityHash2.ToString(System.Globalization.CultureInfo.InvariantCulture)
					+ ") then PackedRaise('12hub: packed payload integrity check failed',0) end;\n";
				vm += "local ByteString=decompress(PackedPayload,PayloadAlphabet);\n";
			}
			else
			{
				vm += "ByteString='";

				StringBuilder sb = new StringBuilder();
				foreach (byte b in bs)
				{
					sb.Append('\\');
					sb.Append(b);
				}

				vm += sb + "';\n";
			}

			int maxConstants = 0;

			void ComputeConstants(Chunk c)
			{
				if (c.Constants.Count > maxConstants)
					maxConstants = c.Constants.Count;
				
				foreach (Chunk _c in c.Functions)
					ComputeConstants(_c);
			}
			
			ComputeConstants(_context.HeadChunk);
			
			// Build-specific checksum parameters make the emitted guard vary on every run.
			// This detects accidental/common payload edits; it is not a cryptographic MAC.
			int integritySeed = RandomNumberGenerator.GetInt32(1, int.MaxValue);
			int integrityMultiplier = RandomNumberGenerator.GetInt32(37, 252);
			int integritySeed2 = RandomNumberGenerator.GetInt32(1, int.MaxValue);
			int integrityMultiplier2 = RandomNumberGenerator.GetInt32(13, 252);
			if (integrityMultiplier2 == integrityMultiplier)
				integrityMultiplier2 = integrityMultiplier2 == 251 ? 13 : integrityMultiplier2 + 1;
			long payloadHash = VMIntegrityCheck.ComputePayloadHash(bs, integritySeed, integrityMultiplier);
			long payloadHash2 = VMIntegrityCheck.ComputePayloadHash(bs, integritySeed2, integrityMultiplier2);
			vm += VMStrings.VMP1
				.Replace("XOR_KEY", _context.PrimaryXorKey.ToString(System.Globalization.CultureInfo.InvariantCulture))
				// Replace suffixed placeholders first so the base names cannot partially consume them.
				.Replace("INTEGRITY_SEED_2", integritySeed2.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Replace("INTEGRITY_MULTIPLIER_2", integrityMultiplier2.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Replace("PAYLOAD_HASH_2", payloadHash2.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Replace("INTEGRITY_SEED", integritySeed.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Replace("INTEGRITY_MULTIPLIER", integrityMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture))
				.Replace("PAYLOAD_HASH", payloadHash.ToString(System.Globalization.CultureInfo.InvariantCulture));
			
			for (int i = 0; i < (int) ChunkStep.StepCount; i++)
			{
				switch (_context.ChunkSteps[i])
				{
					case ChunkStep.ParameterCount:
						vm += "Chunk[4] = gBits8();";
						break;
					case ChunkStep.Constants:
						vm +=
							$@"
								local ConstCount = gBits32()
    							local Consts = {{{string.Join(",", Enumerable.Repeat(0, maxConstants).Select(x => "0"))}}};

								for Idx=1,ConstCount do 
									local Type=gBits8();
									local Cons;
	
									if(Type=={_context.ConstantMapping[1]}) then Cons=(gBits8() ~= 0);
									elseif(Type=={_context.ConstantMapping[2]}) then Cons = gFloat();
									elseif(Type=={_context.ConstantMapping[3]}) then Cons=gString();
									end;
									
									Consts[Idx]=Cons;
								end;
								Chunk[2] = Consts
								";
						break;
					case ChunkStep.Instructions:
						vm +=
							$@"for Idx=1,gBits32() do 
									local Data1=BitXOR(gBits32(),{_context.IXorKey1});
									local Data2=BitXOR(gBits32(),{_context.IXorKey2}); 

									local Type=gBit(Data1,1,2);
									local Opco=gBit(Data2,1,11);
									
									local Inst=
									{{
										Opco,
										gBit(Data1,3,11),
										nil,
										nil,
										Data2
									}};

									if (Type == 0) then Inst[OP_B]=gBit(Data1,12,20);Inst[OP_C]=gBit(Data1,21,29);
									elseif(Type==1) then Inst[OP_B]=gBit(Data2,12,33);
									elseif(Type==2) then Inst[OP_B]=gBit(Data2,12,32)-1048575;
									elseif(Type==3) then Inst[OP_B]=gBit(Data2,12,32)-1048575;Inst[OP_C]=gBit(Data1,21,29);
									end;
									
									Instrs[Idx]=Inst;end;";
						break;
					case ChunkStep.Functions:
						vm += "for Idx=1,gBits32() do Functions[Idx-1]=Deserialize();end;";
						break;
					case ChunkStep.LineInfo:
						if (settings.PreserveLineInfo)
							vm += "for Idx=1,gBits32() do Lines[Idx]=gBits32();end;";
						break;
				}
			}

			vm += "return Chunk;end;";
			vm += settings.PreserveLineInfo ? VMStrings.VMP2_LI : VMStrings.VMP2;

			int maxFunc = 0;

			void ComputeFuncs(Chunk c)
			{
				if (c.Functions.Count > maxFunc)
					maxFunc = c.Functions.Count;
				
				foreach (Chunk _c in c.Functions)
					ComputeFuncs(_c);
			}
			
			ComputeFuncs(_context.HeadChunk);
			
			vm = vm.Replace("FUNC_CNT", string.Join(",", Enumerable.Repeat(0, maxFunc).Select(x => "0")));

			int maxInstrs = 0;

			void ComputeInstrs(Chunk c)
			{
				if (c.Instructions.Count > maxInstrs)
					maxInstrs = c.Instructions.Count;
				
				foreach (Chunk _c in c.Functions)
					ComputeInstrs(_c);
			}
			
			ComputeInstrs(_context.HeadChunk);
			
			vm = vm.Replace("INSTR_CNT", string.Join(",", Enumerable.Repeat(0, maxInstrs).Select(x => "0")));
			
			string GetStr(List<int> opcodes)
			{
				string str = "";
				
				if (opcodes.Count == 1)
					str += $"{virtuals[opcodes[0]].GetObfuscated(_context)}";

				else if (opcodes.Count == 2) 
				{
					if (r.Next(2) == 0)
					{
						str +=
							$"if Enum > {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[1]].GetObfuscated(_context)}";
						str += $" else {virtuals[opcodes[0]].GetObfuscated(_context)}";
						str += "end;";
					}
					else
					{
						str +=
							$"if Enum == {virtuals[opcodes[0]].VIndex} then {virtuals[opcodes[0]].GetObfuscated(_context)}";
						str += $" else {virtuals[opcodes[1]].GetObfuscated(_context)}";
						str += "end;";
					}
				}
				else
				{
					List<int> ordered = opcodes.OrderBy(o => o).ToList();
					// Vary the dispatch-tree topology per build while constraining split ratios
					// so max profiles cannot accidentally create a pathologically deep tree.
					int minSplit = Math.Max(1, ordered.Count / 4);
					int maxSplit = Math.Min(ordered.Count - 1, (ordered.Count * 3) / 4);
					if (maxSplit < minSplit) maxSplit = minSplit;
					int split = r.Next(minSplit, maxSplit + 1);
					List<int> left = ordered.Take(split).ToList();
					List<int> right = ordered.Skip(split).ToList();
					str += "if Enum <= " + left.Last() + " then ";
					str += GetStr(left);
					str += " else ";
					str += GetStr(right);
					// Every recursive split opens an if; close it after the right branch.
					str += " end;";
				}

				return str;
			}

			vm += GetStr(Enumerable.Range(0, virtuals.Count).ToList());
			vm += settings.PreserveLineInfo ? VMStrings.VMP3_LI : VMStrings.VMP3;

			vm = vm.Replace("OP_ENUM", "1")
				.Replace("OP_A", "2")
				.Replace("OP_B", "3")
				.Replace("OP_BX", "4")
				.Replace("OP_C", "5")
				.Replace("OP_DATA", "6")
                .Replace("OPCODE_COUNT", virtuals.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

			
			return vm;
		}
	}
}