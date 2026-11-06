using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuikodenRandomizer.Core;

/// <summary>
/// IDs follow the supplied GSD1 metadata enum. Normal combat membership follows
/// the reviewed project pool (75 rewards); runtime asset/base checks remain required.
/// </summary>
public static class CombatCatalog
{
    public const int TirId = 8;
    public const int TedId = 108;
    public const int OdessaId = 109;

    private static readonly string[] Names =
    {
        "Eileen",
        "Cleo",
        "Gremio",
        "Camille",
        "Kirkis",
        "Mose",
        "Pahn",
        "Luikan",
        "Tir McDohl",
        "Sylvina",
        "Sonya",
        "Anji",
        "Varkas",
        "Ronnie Bell",
        "Kuromimi",
        "Krin",
        "Kasim",
        "Flik",
        "Fukien",
        "Futch",
        "Gen",
        "Humphrey",
        "Kage",
        "Kasumi",
        "Kun To",
        "Kwanda Rosman",
        "Luc",
        "Mathiu",
        "Milich",
        "Milia",
        "Lepant",
        "Sydonia",
        "Tai Ho",
        "Tengaar",
        "Tesla",
        "Viktor",
        "Valeria",
        "Warren",
        "Yam Koo",
        "Juppo",
        "Kessler",
        "Leonardo",
        "Griffith",
        "Kanak",
        "Giovanni",
        "Kimberly",
        "Marie",
        "Lorelai",
        "Alen",
        "Grenshiel",
        "Vincent De Boule",
        "Rubi",
        "Morgan",
        "Clive",
        "Sarah",
        "Fuma",
        "Lotte",
        "Esmeralda",
        "Templeton",
        "Hellion",
        "Apple",
        "Mina",
        "Sheena",
        "Hix",
        "Crowley",
        "Fu Su Lu",
        "Eikei",
        "Kamandol",
        "Quincy",
        "Meese",
        "Maas",
        "Mace",
        "Moose",
        "Blackman",
        "Kreutz",
        "Stallion",
        "Meg",
        "Joshua",
        "Ledon",
        "Taggart",
        "Gon",
        "Kirke",
        "Maximillian",
        "Sancho",
        "Georges",
        "Roc",
        "Melodye",
        "Window",
        "Gaspar",
        "Onil",
        "Zen",
        "Kai",
        "Sergei",
        "Hugo",
        "Ivanov",
        "Sansuke",
        "Jabba",
        "Viki",
        "Qlon",
        "Jeane",
        "Kasios",
        "Antonio",
        "Lester",
        "Marco",
        "Chapman",
        "Chandler",
        "Leon Silverberg",
        "Pesmerga",
        "Ted",
        "Odessa",
    };

    private static readonly int[] CombatIds = { 0, 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 25, 26, 28, 29, 30, 31, 32, 33, 35, 36, 37, 38, 39, 40, 41, 42, 43, 45, 47, 48, 49, 51, 52, 53, 54, 55, 56, 59, 61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 80, 81, 91, 92, 95, 101, 102, 107 };
    private static readonly HashSet<int> CombatSet = new HashSet<int>(CombatIds);
    public static IReadOnlyList<int> NormalCombatIds { get; } = Array.AsReadOnly(CombatIds);

    public static bool IsNormalCombat(int characterId) => CombatSet.Contains(characterId);

    public static string GetName(int characterId) => characterId >= 0 && characterId < Names.Length
        ? Names[characterId]
        : $"Unknown {characterId}";

    public static string Format(int characterId) => $"{characterId}/{GetName(characterId)}";
}
