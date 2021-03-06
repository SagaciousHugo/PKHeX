﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace PKHeX
{
    public class EvolutionTree
    {
        private List<EvolutionSet> Entries { get; } = new List<EvolutionSet>();
        private EvolutionLineage[] Lineage { get; }
        private readonly PersonalTable Personal;

        public EvolutionTree(byte[][] data, GameVersion game, PersonalTable personal)
        {
            Personal = personal;
            switch (game)
            {
                case GameVersion.SM:
                    Entries.AddRange(data.Select(d => new EvolutionSet7(d)));
                    break;
                case GameVersion.ORAS:
                    Entries.AddRange(data.Select(d => new EvolutionSet6(d)));
                    break;
            }
            
            // Create Lineages
            Lineage = new EvolutionLineage[Entries.Count];
            for (int i = 0; i < Entries.Count; i++)
                Lineage[i] = new EvolutionLineage();

            // Populate Lineages
            for (int i = 1; i < Lineage.Length; i++)
            {
                // Iterate over all possible evolutions
                var s = Entries[i];
                foreach (EvolutionMethod evo in s.PossibleEvolutions)
                {
                    int index = getIndex(evo);
                    if (index < 0)
                        continue;

                    var sourceEvo = evo.Copy(i);

                    Lineage[index].Insert(sourceEvo);
                    // If current entries has a pre-evolution, propagate to evolution as well
                    if (Lineage[i].Chain.Count > 0)
                        Lineage[index].Insert(Lineage[i].Chain[0]);

                    if (index >= i) continue;
                    // If destination species evolves into something (ie a 'baby' Pokemon like Cleffa)
                    // Add it to the corresponding parent chains
                    foreach (EvolutionMethod mid in Entries[index].PossibleEvolutions)
                    {
                        int newIndex = getIndex(mid);
                        if (newIndex < 0)
                            continue;

                        Lineage[newIndex].Insert(sourceEvo);
                    }
                }
            }
        }

        private int getIndex(PKM pkm)
        {
            if (pkm.Format < 7)
                return pkm.Species;

            return Personal.getFormeIndex(pkm.Species, pkm.AltForm);
        }
        private int getIndex(EvolutionMethod evo)
        {
            int evolvesToSpecies = evo.Species;
            if (evolvesToSpecies == 0)
                return -1;

            if (Personal == null)
                return evolvesToSpecies;

            int evolvesToForm = evo.Form;
            if (evolvesToForm < 0)
                evolvesToForm = 0;

            return Personal.getFormeIndex(evolvesToSpecies, evolvesToForm);
        }
        public IEnumerable<DexLevel> getValidPreEvolutions(PKM pkm, int lvl)
        {
            int index = getIndex(pkm);
            return Lineage[index].getExplicitLineage(pkm, lvl);
        }
    }

    public abstract class EvolutionSet
    {
        public EvolutionMethod[] PossibleEvolutions;
    }
    public class EvolutionSet6 : EvolutionSet
    {
        private const int SIZE = 6;
        public EvolutionSet6(byte[] data)
        {
            if (data.Length < SIZE || data.Length % SIZE != 0) return;
            PossibleEvolutions = new EvolutionMethod[data.Length / SIZE];
            for (int i = 0; i < data.Length; i += SIZE)
            {
                PossibleEvolutions[i/SIZE] = new EvolutionMethod
                {
                    Method = BitConverter.ToUInt16(data, i + 0),
                    Argument = BitConverter.ToUInt16(data, i + 2),
                    Species = BitConverter.ToUInt16(data, i + 4),

                    // Copy
                    Level = BitConverter.ToUInt16(data, i + 2),
                };
            }
        }
    }
    public class EvolutionSet7 : EvolutionSet
    {
        private const int SIZE = 8;
        public EvolutionSet7(byte[] data)
        {
            if (data.Length < SIZE || data.Length % SIZE != 0) return;
            PossibleEvolutions = new EvolutionMethod[data.Length / SIZE];
            for (int i = 0; i < data.Length; i += SIZE)
            {
                PossibleEvolutions[i / SIZE] = new EvolutionMethod
                {
                    Method = BitConverter.ToUInt16(data, i + 0),
                    Argument = BitConverter.ToUInt16(data, i + 2),
                    Species = BitConverter.ToUInt16(data, i + 4),
                    Form = (sbyte)data[i + 6],
                    Level = data[i + 7],
                };
            }
        }
    }
    public class EvolutionMethod
    {
        public int Method;
        public int Species;
        public int Argument;
        public int Form = -1;
        public int Level;

        public bool RequiresLevelUp;
        public static readonly int[] TradeMethods = {5, 6, 7};

        public bool Valid(PKM pkm, int lvl)
        {
            RequiresLevelUp = false;
            if (Form > -1)
                if (pkm.AltForm != Form)
                    return false;

            switch (Method)
            {
                case 8: // Use Item
                    return true;

                case 5: // Trade Evolution
                case 6: // Trade while Holding
                case 7: // Trade for Opposite Species
                    return !pkm.IsUntraded;
                
                    // Special Levelup Cases
                case 16:
                    if (pkm.CNT_Beauty > Argument)
                        return false;
                    goto default;
                case 23: // Gender = Female
                    if (pkm.Gender != 1)
                        return false;
                    goto default;
                case 24: // Gender = Male
                    if (pkm.Gender != 0)
                        return false;
                    goto default;
                case 34: // Gender = Female, out Form1
                    if (pkm.Gender != 1 || pkm.AltForm != 1)
                        return false;
                    goto default;

                case 36: // Any Time on Version
                case 37: // Daytime on Version
                case 38: // Nighttime on Version
                    if (pkm.Version != Argument && pkm.IsUntraded)
                        return false;
                    goto default;

                default:
                    if (Level == 0 && lvl < 2)
                        return false;
                    if (lvl < Level)
                        return false;

                    RequiresLevelUp = true;

                    // Check Met Level for extra validity
                    switch (pkm.GenNumber)
                    {
                        case 1: // No metdata in RBY
                        case 2: // No metdata in GS, Crystal metdata can be reset
                            return true;
                        case 3:
                        case 4:
                            if (pkm.Format > pkm.GenNumber) // Pal Park / PokeTransfer updates Met Level
                                return true;
                            return pkm.Met_Level < Level;

                        case 5: // Bank keeps current level
                        case 6:
                        case 7:
                            return lvl >= Level && (!pkm.IsNative || pkm.Met_Level < Level);

                    }
                    return false;
            }
        }

        public DexLevel GetDexLevel(int lvl)
        {
            return new DexLevel
            {
                Species = Species,
                Level = lvl,
                Form = Form,
                Flag = Method,
            };
        }

        public EvolutionMethod Copy(int species)
        {
            return new EvolutionMethod
            {
                Method = Method,
                Species = species,
                Argument = Argument,
                Form = Form,
                Level = Level
            };
        }
    }

    // Informatics
    public class EvolutionLineage
    {
        public readonly List<EvolutionStage> Chain = new List<EvolutionStage>();

        public void Insert(EvolutionMethod entry)
        {
            int matchChain = -1;
            for (int i = 0; i < Chain.Count; i++)
                if (Chain[i].StageEntryMethods.Any(e => e.Species == entry.Species))
                    matchChain = i;

            if (matchChain != -1)
                Chain[matchChain].StageEntryMethods.Add(entry);
            else
                Chain.Insert(0, new EvolutionStage { StageEntryMethods = new List<EvolutionMethod> {entry}});
        }
        public void Insert(EvolutionStage evo)
        {
            Chain.Insert(0, evo);
        }

        public IEnumerable<DexLevel> getExplicitLineage(PKM pkm, int lvl)
        {
            List<DexLevel> dl = new List<DexLevel> { new DexLevel { Species = pkm.Species, Level = lvl, Form = pkm.AltForm } };
            foreach (var link in Chain)
            {
                foreach (var evo in link.StageEntryMethods)
                {
                    if (!evo.Valid(pkm, lvl))
                        continue;

                    dl.Add(evo.GetDexLevel(lvl));
                    if (evo.RequiresLevelUp)
                        lvl--;
                    break;
                }
            }
            return dl;
        }
    }
    public class EvolutionStage
    {
        public List<EvolutionMethod> StageEntryMethods;
    }
}
