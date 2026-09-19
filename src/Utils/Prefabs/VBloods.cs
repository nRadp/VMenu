using System.Collections.Generic;
using Stunlock.Core;

namespace ExtrasensoryPerception.Utils.Prefabs;

// https://github.com/CrimsonMods/VAMP/blob/master/Data/VBloods.cs
// by SkyTech6
public static class VBloods
{
    private static readonly Dictionary<PrefabGUID, (string Long, string Short)> PrefabToNames = new()
    {
        { new PrefabGUID(1124739990), ("Keely the Frost Archer", "Keely") },
        { new PrefabGUID(2122229952), ("Rufus the Foreman", "Rufus") },
        { new PrefabGUID(-2025101517), ("Errol the Stonebreaker", "Errol") },
        { new PrefabGUID(763273073), ("Lidia the Chaos Archer", "Lidia") },
        { new PrefabGUID(577478542), ("Goreswine the Ravager", "Goreswine") },
        { new PrefabGUID(1106149033), ("Grayson the Armourer", "Grayson") },
        { new PrefabGUID(-2039908510), ("Nibbles the Putrid Rat", "Nibbles") },
        { new PrefabGUID(1896428751), ("Clive the Firestarter", "Clive") },
        { new PrefabGUID(153390636), ("Nicholaus the Fallen", "Nicholaus") },
        { new PrefabGUID(-1659822956), ("Quincey the Bandit King", "Quincey") },
        { new PrefabGUID(-1942352521), ("Beatrice the Tailor", "Beatrice") },
        { new PrefabGUID(-29797003), ("Vincent the Frostbringer", "Vincent") },
        { new PrefabGUID(-1449631170), ("Tristan the Vampire Hunter", "Tristan") },
        { new PrefabGUID(939467639), ("Leandra the Shadow Priestess", "Leandra") },
        { new PrefabGUID(-1065970933), ("Terah the Geomancer", "Terah") },
        { new PrefabGUID(850622034), ("Meredith the Bright Archer", "Meredith") },
        { new PrefabGUID(24378719), ("Frostmaw the Mountain Terror", "Frostmaw") },
        { new PrefabGUID(1688478381), ("Octavian the Militia Captain", "Octavian") },
        { new PrefabGUID(-680831417), ("Raziel the Shepherd", "Raziel") },
        { new PrefabGUID(-548489519), ("Ungora the Spider Queen", "Ungora") },
        { new PrefabGUID(-203043163), ("Albert the Duke of Balaton", "Albert") },
        { new PrefabGUID(-1968372384), ("Jade the Vampire Hunter", "Jade") },
        { new PrefabGUID(-1208888966), ("Foulrot the Soultaker", "Foulrot") },
        { new PrefabGUID(-1505705712), ("Willfred the Werewolf Chief", "Willfred") },
        { new PrefabGUID(-2013903325), ("Mairwyn the Elementalist", "Mairwyn") },
        { new PrefabGUID(-1347412392), ("Terrorclaw the Ogre", "Terrorclaw") },
        { new PrefabGUID(685266977), ("Morian the Stormwing Matriarch", "Morian") },
        { new PrefabGUID(-910296704), ("Matka the Curse Weaver", "Matka") },
        { new PrefabGUID(1112948824), ("Lord Styx the Night Champion", "Styx") },
        { new PrefabGUID(-1936575244), ("Gorecrusher the Behemoth", "Gorecrusher") },
        { new PrefabGUID(-393555055), ("Talzur the Winged Horror", "Talzur") },
        { new PrefabGUID(114912615), ("Azariel the Sunbringer", "Azariel") },
        { new PrefabGUID(-26105228), ("Sir Magnus the Overseer", "Sir Magnus") },
        { new PrefabGUID(-740796338), ("Solarus the Immaculate", "Solarus") },
        { new PrefabGUID(192051202), ("Baron du Bouchon the Sommelier", "Baron") },
        { new PrefabGUID(-1391546313), ("Kodia the Ferocious Bear", "Kodia") },
        { new PrefabGUID(-1905691330), ("Alpha the White Wolf", "Alpha") },
        { new PrefabGUID(172235178), ("Ziva the Engineer", "Ziva") },
        { new PrefabGUID(1233988687), ("Adam the Firstborn", "Adam") },
        { new PrefabGUID(106480588), ("Angram the Purifier", "Angram") },
        { new PrefabGUID(2054432370), ("Voltatia the Power Master", "Voltatia") },
        { new PrefabGUID(814083983), ("Henry Blackbrew the Doctor", "Henry") },
        { new PrefabGUID(-1101874342), ("Domina the Blade Dancer", "Domina") },
        { new PrefabGUID(910988233), ("Grethel the Glassblower", "Grethel") },
        { new PrefabGUID(-1373413273), ("Brutus the Watcher", "Brutus") },
        { new PrefabGUID(-784265984), ("Boyo", "Boyo") },
        { new PrefabGUID(-99012450), ("Christina the Sun Priestess", "Christina") },
        { new PrefabGUID(1945956671), ("Maja the Dark Savant", "Maja") },
        { new PrefabGUID(-484556888), ("Polora the Feywalker", "Polora") },
        { new PrefabGUID(326378955), ("Cyril the Cursed Smith", "Cyril") },
        { new PrefabGUID(613251918), ("Bane the Shadowblade", "Bane") },
        { new PrefabGUID(-1365931036), ("Kriig the Undead General", "Kriig") },
        { new PrefabGUID(109969450), ("Ben the Old Wanderer", "Ben") },
        { new PrefabGUID(-2122682556), ("Finn the Fisherman", "Finn") },
        { new PrefabGUID(336560131), ("Simon Belmont the Vampire Hunter", "Simon") },
        { new PrefabGUID(495971434), ("General Valencia the Depraved", "Valencia") },
        { new PrefabGUID(-327335305), ("Dracula the Immortal King", "Dracula") },
        { new PrefabGUID(-496360395), ("General Cassius the Betrayer", "Cassius") },
        { new PrefabGUID(795262842), ("General Elena the Hollow", "Elena") },
        { new PrefabGUID(619948378), ("Sir Erwin the Gallant Cavalier", "Erwin") },
        { new PrefabGUID(-753453016), ("Gaius The Cursed Champion", "Gaius") },
        { new PrefabGUID(-1383529374), ("Jakira the Shadow Huntress", "Jakira") },
        { new PrefabGUID(-1669199769), ("Stavros the Carver", "Stavros") },
        { new PrefabGUID(173259239), ("Dantos the Forgebinder", "Dantos") },
        { new PrefabGUID(591725925), ("Megara the Serpent Queen", "Megara") },
        { new PrefabGUID(1295855316), ("Lucile the Venom Alchemist", "Lucile") }
    };

    /// <summary>
    /// Converts a PrefabGUID to its full V Blood name
    /// </summary>
    /// <param name="prefab">The PrefabGUID to convert</param>
    /// <returns>The full name of the V Blood</returns>
    public static string GetName(PrefabGUID prefab)
    {
        if (PrefabToNames.TryGetValue(prefab, out var names))
            return names.Long;
        return "Unknown VBlood";
    }

    /// <summary>
    /// Converts a GUID integer to its full V Blood name
    /// </summary>
    /// <param name="guid">The GUID integer to convert</param>
    /// <returns>The full name of the V Blood</returns>
    public static string GetName(int guid)
    {
        return GetName(new PrefabGUID(guid));
    }

    /// <summary>
    /// Converts a PrefabGUID to its short V Blood name
    /// </summary>
    /// <param name="prefab">The PrefabGUID to convert</param>
    /// <returns>The short name of the V Blood</returns>
    public static string GetShortName(PrefabGUID prefab)
    {
        if (PrefabToNames.TryGetValue(prefab, out var names))
            return names.Short;
        return "Unknown VBlood ";
    }

    /// <summary>
    /// Converts a GUID integer to its short V Blood name
    /// </summary>
    /// <param name="guid">The GUID integer to convert</param>
    /// <returns>The short name of the V Blood</returns>
    public static string GetShortName(int guid)
    {
        return GetShortName(new PrefabGUID(guid));
    }
}