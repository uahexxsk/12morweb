using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Opcodes
{
	//custom VM opcode for inlining
	public class OpSetTop : VOpcode
	{
		public override bool IsInstruction(Instruction instruction) =>
			instruction.OpCode == Opcode.SetTop;

		public override string GetObfuscated(ObfuscationContext context) =>
			"Top=Inst[OP_A];";
	}
}