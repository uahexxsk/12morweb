using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hub12.Bytecode_Library.IR;
using Hub12.Obfuscator;

namespace Hub12.Bytecode_Library.Bytecode
{
	public class Serializer
	{
		private ObfuscationContext _context;
		private ObfuscationSettings _settings;
		private Random _r = Hub12.TwelveHubEngine.Random;
		private Encoding _fuckingLua = Encoding.GetEncoding(28591);

		public Serializer(ObfuscationContext context, ObfuscationSettings settings)
		{
			_context = context;
			_settings = settings;
		}
		
		public byte[] SerializeLChunk(Chunk chunk, bool factorXor = true)
		{
			List<byte> bytes = new List<byte>();
			// Rolling XOR: each plaintext byte becomes the key for the next byte.
			// Only the root call encodes; nested chunks are encoded by their parent.
			byte rollingKey = (byte)_context.PrimaryXorKey;

			void WriteByte(byte b)
			{
				byte plain = b;
				if (factorXor)
				{
					b = (byte)(plain ^ rollingKey);
					rollingKey = plain;
				}
				bytes.Add(b);
			}

			void Write(byte[] b, bool checkEndian = true)
			{
				if (!BitConverter.IsLittleEndian && checkEndian)
					b = b.Reverse().ToArray();

				foreach (byte value in b)
				{
					if (!factorXor)
					{
						bytes.Add(value);
						continue;
					}

					bytes.Add((byte)(value ^ rollingKey));
					rollingKey = value;
				}
			}
			
			void WriteInt32(int i) =>
				Write(BitConverter.GetBytes(i));
			
			void WriteNumber(double d) =>
				Write(BitConverter.GetBytes(d));
			
			void WriteString(string s)
			{
				byte[] sBytes = _fuckingLua.GetBytes(s);
				
				WriteInt32(sBytes.Length);
				Write(sBytes, false);
			}
						
			void WriteBool(bool b) =>
				Write(BitConverter.GetBytes(b));

			int[] SerializeInstruction(Instruction inst)
			{
				inst.UpdateRegisters();
							
				if (inst.InstructionType == InstructionType.Data)
				{
					return new[]
					       {
						       _r.Next(),
						       inst.Data
					       };
				}

				var cData = inst.CustomData;
				int opCode = (int)inst.OpCode;
				
				if (cData != null)
				{
					var virtualOpcode = cData.Opcode;
					
					opCode = cData.WrittenOpcode?.VIndex ?? virtualOpcode.VIndex;
					virtualOpcode?.Mutate(inst);
				}
				
				int a = inst.A;
				int b = inst.B;
				int c = inst.C;
				
				int result_i1 = 0;
				int result_i2 = 0;

				if (inst.InstructionType == InstructionType.AsBx || inst.InstructionType == InstructionType.AsBxC)
					b += 1048575;
			
				result_i1 |= (byte) inst.InstructionType;
				result_i1 |= ((a & 0x1FF) << 2);
				result_i1 |= ((b & 0x1FF) << (2     + 9));
				result_i1 |= (c           << (2 + 9 + 9));

				result_i2 |= opCode;
				result_i2 |= (b         << 11);

				return new[] { result_i1, result_i2 };
			}
			
			// Randomize constant order per function chunk before serialization.
			// Use an in-place Fisher-Yates shuffle because List<Constant> has no
			// Shuffle() extension in this project. Operand indexes are refreshed
			// from the updated ConstantMap below.
			for (int shuffleIndex = chunk.Constants.Count - 1; shuffleIndex > 0; shuffleIndex--)
			{
				int swapIndex = _r.Next(shuffleIndex + 1);
				Constant tempConstant = chunk.Constants[shuffleIndex];
				chunk.Constants[shuffleIndex] = chunk.Constants[swapIndex];
				chunk.Constants[swapIndex] = tempConstant;
			}
			chunk.UpdateMappings();
			
			for (int i = 0; i < (int) ChunkStep.StepCount; i++)
			{
				switch (_context.ChunkSteps[i])
				{
					case ChunkStep.ParameterCount:
						WriteByte(chunk.ParameterCount);
						break;
					case ChunkStep.Constants:
						WriteInt32(chunk.Constants.Count);
						foreach (Constant c in chunk.Constants)
						{
							WriteByte((byte)_context.ConstantMapping[(int)c.Type]);
							switch (c.Type)
							{
								case ConstantType.Boolean:
									WriteBool(c.Data);
									break;
								case ConstantType.Number:
									WriteNumber(c.Data);	
									break;
								case ConstantType.String:
									WriteString(c.Data);
									break;
							}
						}
						break;
					case ChunkStep.Instructions:
						WriteInt32(chunk.Instructions.Count);
						
						foreach (Instruction ins in chunk.Instructions)
						{
							int[] arr = SerializeInstruction(ins);
							
							//WriteByte((byte)ins.Instruction.InstructionType);
							WriteInt32(arr[0] ^ _context.IXorKey1);
							WriteInt32(arr[1] ^ _context.IXorKey2);
						}
						break;
					case ChunkStep.Functions:
						WriteInt32(chunk.Functions.Count);
						foreach (Chunk c in chunk.Functions)
							Write(SerializeLChunk(c, false));
						
						break;
					case ChunkStep.LineInfo when _settings.PreserveLineInfo:
						WriteInt32(chunk.Instructions.Count);
						foreach (var instr in chunk.Instructions)
							WriteInt32(instr.Line);	
						break;
				}
			}
			
			return bytes.ToArray();
		}
	}
}