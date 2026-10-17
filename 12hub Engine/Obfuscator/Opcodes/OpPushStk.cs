using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Opcodes
{
	public class OpPushStk : VOpcode
	{
		public override bool IsInstruction(Instruction instruction) =>
			instruction.OpCode == Opcode.PushStack;

		public override string GetObfuscated(ObfuscationContext context) =>
			"Stk[Inst[OP_A]] = Stk";
	}
}