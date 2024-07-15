using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;

namespace BetterQuestObjectivesPatcher
{
    public class Program
    {
        private const string BqoEsp = "BetterQuestObjectives.esp";

        private static readonly List<string> BasicMasters = new()
        {
            "Skyrim.esm",
            "Update.esm",
            "Dawnguard.esm",
            "HearthFires.esm",
            "Dragonborn.esm",
            "Unofficial Skyrim Special Edition Patch" // Requiem has this as a master
        };
        
        public static async Task<int> Main(string[] args)
        {
            return await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "BetterQuestObjectivesPatcher.esp")
                .Run(args);
        }

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {

            // var bqoMod = state.LoadOrder.GetIfEnabledAndExists(BQO_ESP);
            
            var quests = state
                .LoadOrder
                .PriorityOrder
                .Quest()
                .WinningContextOverrides();
            
            foreach (var questCtx in quests)
            {
                Console.WriteLine($"Processing mod ${questCtx.ModKey.FileName}");
                var ctxMod = state.LoadOrder.GetIfEnabledAndExists(questCtx.ModKey);
                if (IsBqoPatch(ctxMod))
                {
                    continue;
                }

                var questLinkGetter = questCtx.Record.ToLinkGetter();
                var allContexts = questLinkGetter.ResolveAllContexts<ISkyrimMod, ISkyrimModGetter, IQuest, IQuestGetter>(state.LinkCache);
                
                // Resolve context for quest
                // Make sure quest has the following in its context:
                // 1. an entry in a base ESP
                // 2. an entry in BQO or one of its patches
                // 3. something else overwriting BQO
                IModContext<ISkyrimMod,ISkyrimModGetter,IQuest,IQuestGetter> baseGameContext;
                IModContext<ISkyrimMod,ISkyrimModGetter,IQuest,IQuestGetter> bqoContext;
                // third mod context is questCtx

                var modContexts = allContexts.ToList();
                try
                {
                    baseGameContext = modContexts.First(context => IsBasicMaster(context.ModKey));
                }
                catch (InvalidOperationException e)
                {
                    continue;
                }

                try
                {
                    bqoContext = modContexts.First(context =>
                    {
                        var modInContextListing = state.LoadOrder.GetIfEnabled(context.ModKey);
                        return IsBqoPatch(modInContextListing.Mod!);
                    });
                }
                catch (InvalidOperationException e)
                {
                    continue;
                }
                

                var winningObjectives = questCtx.Record.Objectives;
                var baseObjectives = baseGameContext.Record.Objectives;
                var bqoObjectives = bqoContext.Record.Objectives;

                if (winningObjectives.Count != baseObjectives.Count || winningObjectives.Count != bqoObjectives.Count)
                {
                    continue;
                }
                
                // If those 3 things are true
                // For each quest obective
                // TODO: Clean up this nasty CS101 loop
                var copiedAsOverride = false;
                IQuest? patchedQuest = null;
                for (var i = 0; i < winningObjectives.Count; i++)
                {
                    // If NNAM of winning override == NNAM of basic master (skyrim, dg, etc)
                    if (!Equals(winningObjectives[i].DisplayText!.String, baseObjectives[i].DisplayText!.String))
                    {
                        continue;
                    }
                    // AND if NNAM is different in bqo
                    if (Equals(bqoObjectives[i].DisplayText!.String, winningObjectives[i].DisplayText!.String)) continue;
                    // Bail out if any null
                    if (winningObjectives[i].DisplayText == null || bqoObjectives[i].DisplayText == null || baseObjectives[i].DisplayText == null)
                    {
                        continue;
                    }
                            
                    if (!copiedAsOverride)
                    {
                        patchedQuest = questCtx.GetOrAddAsOverride(state.PatchMod);
                        copiedAsOverride = true;
                    }

                    if (patchedQuest != null)
                    {
                        patchedQuest.Objectives[i].DisplayText = bqoObjectives[i].DisplayText!.DeepCopy();
                    }
                }
            }
        }

        private static bool IsBqoPatch(IModGetter mod)
        {
            return mod.ModKey.FileName.Equals(BqoEsp)
                   || mod.MasterReferences.Any(reference => reference.Master.FileName.Equals(BqoEsp));
        }
        
        private static bool IsBasicMaster(ModKey mod)
        {
            return BasicMasters.Contains(mod.FileName);
        }
    }
}
