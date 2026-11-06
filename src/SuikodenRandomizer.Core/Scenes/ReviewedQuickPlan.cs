using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core.Scenes;

public enum QuickStepKind { Presentation, SelectRecruitment, NativeSound, NativeBranch }

public sealed class QuickStep
{
    internal QuickStep(int offset, int opcode, int next, QuickStepKind kind)
    { Offset = offset; Opcode = opcode; Next = next; Kind = kind; }
    public int Offset { get; }
    public int Opcode { get; }
    public int Next { get; }
    public QuickStepKind Kind { get; }
}

/// <summary>
/// Reviewed pre-grant paths for Sergei and Sansuke only. Does not evaluate
/// eligibility, write membership, or skip post-grant work. Native branching
/// remains responsible for its own cursor; it receives an explicit join result.
/// </summary>
public sealed class ReviewedQuickPlan
{
    private ReviewedQuickPlan(RecruitmentSceneDefinition definition, int end, QuickStep[] steps)
    { Definition = definition; EndOffset = end; Steps = Array.AsReadOnly(steps); }
    public RecruitmentSceneDefinition Definition { get; }
    public int EndOffset { get; }
    public ReadOnlyCollection<QuickStep> Steps { get; }
    public QuickStep? At(int opcodeOffset, int opcode) =>
        Steps.FirstOrDefault(s => s.Offset == opcodeOffset && s.Opcode == opcode);

    public static ReviewedQuickPlan? For(RecruitmentSceneDefinition definition)
    {
        if (definition is null) return null;
        int[] offsets;
        int end;
        if (definition.Key == "vb2:5:10" && definition.SourceId == 92)
        { offsets = new[] { 0,2,5,9,10,11,13,16,20,24,25,28,30,33,37,38,39,41,43,49,52,56,57,59 }; end = 60; }
        else if (definition.Key == "vb6:8:1" && definition.SourceId == 95)
        { offsets = new[] { 0,2,5,9,10,11,12,14,16,19,23,27,28,31,32,34,36,39,43,44,45,46,48,50,52,58,61,65,66,68 }; end = 69; }
        else return null;
        // Full matching happens before admission. Validate the generated plan
        // too, so an edited registry cannot silently introduce gameplay skips.
        var steps = new List<QuickStep>();
        for (int i = 0; i < offsets.Length; i++)
        {
            int start = offsets[i], next = i + 1 < offsets.Length ? offsets[i + 1] : end;
            int opcode = definition.Commands[start];
            QuickStepKind kind;
            int width;
            switch (opcode)
            {
                case 0x00: width = 6; kind = QuickStepKind.Presentation; break;
                case 0x01: case 0x0A: case 0x0B: case 0x17:
                    width = 2; kind = QuickStepKind.Presentation; break;
                case 0x02: case 0x0C: case 0x18: case 0x46:
                    width = 1; kind = QuickStepKind.Presentation; break;
                case 0x12: width = 3; kind = QuickStepKind.Presentation; break;
                case 0x1C: width = 4; kind = QuickStepKind.Presentation; break;
                case 0x05:
                    width = 4; kind = QuickStepKind.SelectRecruitment;
                    if (definition.Commands[start+1] != 0 || definition.Commands[start+2] != 2 || definition.Commands[start+3] != 1)
                        throw new InvalidOperationException("Reviewed two-choice contract changed.");
                    break;
                case 0x41:
                    width = 2; kind = QuickStepKind.NativeSound;
                    if (definition.Commands[start+1] != 0x96) throw new InvalidOperationException("Reviewed sound differs.");
                    break;
                case 0x2F:
                    // Native GoSentakuKekkaS with 0x82 and result zero selects
                    // the first branch at offset+3. It executes; we verify its
                    // actual resulting cursor rather than bypassing it.
                    width = 3; kind = QuickStepKind.NativeBranch;
                    if (definition.Commands[start+1] != 0x82 || definition.Commands[start+2] != 0xFC)
                        throw new InvalidOperationException("Reviewed choice branch changed.");
                    break;
                default: throw new InvalidOperationException("Unapproved opcode inside quick span: " + opcode);
            }
            if (start + width != next) throw new InvalidOperationException("Non-contiguous reviewed quick span.");
            steps.Add(new QuickStep(start, opcode, next, kind));
        }
        if (definition.Commands[end] != 0x45 || definition.Commands[end+1] != definition.SourceId
            || steps.Count(s => s.Kind == QuickStepKind.SelectRecruitment) != 1
            || steps.Count(s => s.Kind == QuickStepKind.NativeBranch) != 1
            || steps.Count(s => s.Kind == QuickStepKind.NativeSound) != 1)
            throw new InvalidOperationException("Reviewed grant boundary/branch structure changed.");
        return new ReviewedQuickPlan(definition, end, steps.ToArray());
    }
}
