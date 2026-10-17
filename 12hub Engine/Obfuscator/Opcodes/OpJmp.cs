using Hub12.Bytecode_Library.Bytecode;
using Hub12.Bytecode_Library.IR;

namespace Hub12.Obfuscator.Opcodes
{
	public class OpJmp : VOpcode
	{
		public override bool IsInstruction(Instruction instruction) =>
			instruction.OpCode == Opcode.Jmp;

		public override string GetObfuscated(ObfuscationContext context) =>
			"InstrPoint=InstrPoint+Inst[OP_B];";
	}
}