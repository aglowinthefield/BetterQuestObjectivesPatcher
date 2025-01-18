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
            "unofficial skyrim special edition patch.esp" // Requiem has this as a master
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
                IEnumerable<IModContext<ISkyrimMod, ISkyrimModGetter, IQuest, IQuestGetter>> baseGameContexts = allContexts.Where(context => IsBasicMaster(context.ModKey));
                if (!baseGameContexts.Any()) continue;
                IModContext<ISkyrimMod, ISkyrimModGetter, IQuest, IQuestGetter> bqoContext;
                // third mod context is questCtx

                try
                {
                    bqoContext = allContexts.First(context => IsBqoPatch(state.LoadOrder.GetIfEnabled(context.ModKey).Mod!));
                }
                catch (InvalidOperationException e)
                {
                    continue;
                }


                IQuest? patchedQuest = null;

                var winningObjectives = questCtx.Record.Objectives;
                var allBaseObjectives = baseGameContexts.Select(context => context.Record.Objectives);
                var bqoObjectives = bqoContext.Record.Objectives;
                // only iterate through objectives modified by BQO
                foreach (var bqoObjective in bqoObjectives)
                {
                    // get all versions of text from the base ignoring capitalization (BQO should overwrite text that matches any base)
                    var baseTexts = allBaseObjectives.SelectMany(objectives => objectives.Where(o => o.Index == bqoObjective.Index).Select(o => o.DisplayText?.String?.ToLower()));
                    // find all objectives in winner that match a base or BQO (ignoring capitalization)
                    var matchingObjectives = winningObjectives.Where(o =>
                    {
                        return baseTexts.Contains(o.DisplayText?.String?.ToLower()) ||
                        Equals(o.DisplayText?.String?.ToLower(), bqoObjective.DisplayText?.String?.ToLower());
                    });
                    /* find closest objective in winner to current objective in BQO by index
                       reason: in case mod author changes index, example: AYOP - Companions
                       hopefully this doesn't cause problematic patches if a mod author adds other quest objectives with ambiguous descriptions
                       ideal solution is matchingObjectives.FirstOrDefault(o => o.Index == bqoObjective.Index) to get exact match with a continue for null case
                       the same logic may be needed for stages below, but currently there are no known cases */
                    if (!matchingObjectives.Any()) continue;
                    var winningObjective = matchingObjectives.Aggregate((closest, next) => Math.Abs(next.Index - bqoObjective.Index) < Math.Abs(closest.Index - bqoObjective.Index) ? next : closest);
                    // skip if winner identical to BQO
                    if (Equals(bqoObjective.DisplayText, winningObjective.DisplayText)) continue;

                    patchedQuest ??= questCtx.GetOrAddAsOverride(state.PatchMod);
                    patchedQuest.Objectives.Where(o => o.Index == winningObjective.Index).First().DisplayText = bqoObjective.DisplayText!.DeepCopy();
                }

                // patch Log Entries for Stages
                var winningStages = questCtx.Record.Stages;
                var allBaseStages = baseGameContexts.Select(context => context.Record.Stages);
                var bqoStages = bqoContext.Record.Stages;
                // only iterate through stages modified by BQO
                foreach (var bqoStage in bqoStages)
                {
                    var baseStages = allBaseStages.SelectMany(stages => stages.Where(s => s.Index == bqoStage.Index));
                    // iterate over each log entry that appears in the BQO version
                    for (var i = 0; i < bqoStage.LogEntries.Count; i++)
                    {
                        // get all versions of text from the base ignoring capitalization (BQO should overwrite text that matches any base)
                        var baseTexts = baseStages.Where(s => i < s.LogEntries.Count).Select(s => s.LogEntries[i].Entry?.String?.ToLower());
                        // find all objectives in winner that match a base or BQO (ignoring capitalization)
                        var matchingStages = winningStages.Where(s =>
                        {
                            return baseTexts.Contains(s.LogEntries.ElementAtOrDefault(i)?.Entry?.String?.ToLower()) ||
                            Equals(s.LogEntries.ElementAtOrDefault(i)?.Entry?.String?.ToLower(), bqoStage.LogEntries[i].Entry?.String?.ToLower());
                        });
                        // find stage with index that matches BQO
                        var winningStage = matchingStages.FirstOrDefault(s => s.Index == bqoStage.Index);
                        if (winningStage == null) continue;
                        // skip if winner identical to BQO
                        if (Equals(bqoStage.LogEntries[i].Entry, winningStage.LogEntries[i].Entry)) continue;

                        patchedQuest ??= questCtx.GetOrAddAsOverride(state.PatchMod);
                        patchedQuest.Stages.Where(s => s.Index == winningStage.Index).First().LogEntries[i].Entry = bqoStage.LogEntries[i].Entry!.DeepCopy();
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
