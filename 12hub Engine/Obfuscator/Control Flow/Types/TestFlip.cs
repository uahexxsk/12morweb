using System;
using System.Collections.Generic;
using System.Linq;
using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;
using Hub12.Extensions;

namespace Hub12.Obfuscator.Control_Flow.Types
{
	public static class TestFlip
	{
		public static void DoInstructions(Chunk chunk, List<Instruction> instructions)
		{
			instructions = instructions.ToList();
			
			CFGenerator generator = new CFGenerator();
			Random r = Hub12.TwelveHubEngine.Random;

			for (int idx = instructions.Count - 1; idx >= 0; idx--)
			{
				Instruction i = instructions[idx];
				switch (i.OpCode)
				{
					case Opcode.Lt:
					case Opcode.Le:
					case Opcode.Eq:
					{
						if (r.Next(2) == 1)
						{
							i.A = i.A == 0 ? 1 : 0;
							Instruction nJmp = generator.NextJMP(chunk, instructions[idx + 2]);
							chunk.Instructions.Insert(chunk.InstructionMap[i] + 1, nJmp);
						}
						
						break;
					}

					case Opcode.Test:
					{
						if (r.Next(2) == 1) {
							i.C = i.C == 0 ? 1 : 0;
							Instruction nJmp = generator.NextJMP(chunk, instructions[idx + 2]);
							chunk.Instructions.Insert(chunk.InstructionMap[i] + 1, nJmp);
						}

						break;
					}
				}
			}
			
			chunk.UpdateMappings();
		}
	}
}