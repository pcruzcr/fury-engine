// Shared deterministic input generator for F0 tests.
// Pure arithmetic pattern: no System.Random, fully reproducible by frame index.
using Fighter.Core;

namespace Fighter.Core.Tests;

internal static class InputGen
{
    private static readonly Buttons[] Cycle =
        { Buttons.None, Buttons.LP, Buttons.MP, Buttons.HP, Buttons.LK, Buttons.MK, Buttons.HK,
          Buttons.LP | Buttons.LK, Buttons.MP | Buttons.HP };

    public static InputState P1(int frame) => new(
        Cycle[frame % Cycle.Length],
        (frame / 3) % 3 - 1,
        (frame / 7) % 3 - 1,
        frame);

    public static InputState P2(int frame) => new(
        Cycle[(frame * 2 + 1) % Cycle.Length],
        -(((frame / 5) % 3) - 1),
        ((frame / 11) % 3) - 1,
        frame);
}
