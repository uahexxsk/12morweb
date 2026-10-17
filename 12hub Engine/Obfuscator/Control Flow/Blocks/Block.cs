using System.Collections.Generic;
using System.Linq;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Control_Flow.Blocks
{
	public class Block
	{
		public Chunk Chunk;
		public List<Instruction> Body = new List<Instruction>();
		public Block Successor = null;

		public Block(Chunk c) =>
			Chunk = c;
	}
}