using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Opcodes
{
	public class OpSetUpval : VOpcode
	{
		public override bool IsInstruction(Instruction instruction) =>
			instruction.OpCode == Opcode.SetUpval;

		public override string GetObfuscated(ObfuscationContext context) =>
			"Upvalues[Inst[OP_B]]=Stk[Inst[OP_A]];";
	}
}