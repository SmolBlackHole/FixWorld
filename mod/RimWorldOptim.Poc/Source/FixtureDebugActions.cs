using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimWorldOptim.Poc
{
    internal static class FixtureDebugActions
    {
        private const string Category = "RimWorldOptim";
        private const int ExpectedMapSize = 250;

        [DebugAction(Category, "Create catalog control (fresh map)",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void CreateCatalogControl()
        {
            Map map = Find.CurrentMap;
            string rejectionReason;
            if (!CanReplaceWithCatalogControl(map, out rejectionReason))
            {
                Messages.Message(
                    "Catalog control not created: " + rejectionReason,
                    MessageTypeDefOf.RejectInput,
                    false);
                return;
            }

            Autotests_ColonyMaker.MakeColony_Full();
            WriteActivityReport(map, "catalog-control-v1");
        }

        [DebugAction(Category, "Report fixture activity",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportFixtureActivity()
        {
            WriteActivityReport(Find.CurrentMap, "current-map");
        }

        private static bool CanReplaceWithCatalogControl(Map map, out string reason)
        {
            if (map == null)
            {
                reason = "no current map";
                return false;
            }

            if (map.Size.x != ExpectedMapSize || map.Size.z != ExpectedMapSize)
            {
                reason = string.Format(
                    CultureInfo.InvariantCulture,
                    "expected a {0}x{0} quick-test map, got {1}x{2}",
                    ExpectedMapSize,
                    map.Size.x,
                    map.Size.z);
                return false;
            }

            List<Pawn> playerPawns = map.mapPawns.AllPawnsSpawned
                .Where(pawn => pawn.Faction == Faction.OfPlayer)
                .ToList();
            bool hasUnexpectedPlayerPawn = playerPawns.Any(
                pawn => !Find.GameInfo.startingAndOptionalPawns.Contains(pawn));
            bool hasUnexpectedPlayerThing = map.listerThings.AllThings.Any(
                thing => !(thing is Pawn) && thing.Faction == Faction.OfPlayer);
            bool hasPlayerZone = map.zoneManager.AllZones.Count != 0;
            bool hasDesignation = map.designationManager.AllDesignations.Count != 0;

            if (hasUnexpectedPlayerPawn || hasUnexpectedPlayerThing || hasPlayerZone || hasDesignation)
            {
                reason = "the map contains player state beyond the untouched starting pawns";
                return false;
            }

            reason = null;
            return true;
        }

        private static void WriteActivityReport(Map map, string fixtureId)
        {
            IReadOnlyList<Pawn> allPawns = map.mapPawns.AllPawnsSpawned;
            List<Pawn> playerPawns = allPawns
                .Where(pawn => pawn.Faction == Faction.OfPlayer)
                .ToList();
            List<Pawn> freeColonists = playerPawns
                .Where(pawn => pawn.IsFreeColonist)
                .ToList();
            List<Building_WorkTable> workTables = map.listerThings.AllThings
                .OfType<Building_WorkTable>()
                .Where(table => table.Spawned)
                .ToList();
            List<Building_Door> doors = map.listerThings.AllThings
                .OfType<Building_Door>()
                .Where(door => door.Spawned)
                .ToList();
            List<Zone_Stockpile> stockpiles = map.zoneManager.AllZones
                .OfType<Zone_Stockpile>()
                .ToList();
            List<Zone_Growing> growingZones = map.zoneManager.AllZones
                .OfType<Zone_Growing>()
                .ToList();
            List<PowerNet> powerNets = map.powerNetManager.AllNetsListForReading;

            int activeBills = workTables.Sum(table => table.BillStack.Count);
            int growingCells = growingZones.Sum(zone => zone.Cells.Count);
            int plantedGrowingCells = growingZones.Sum(zone =>
                zone.Cells.Count(cell => cell.GetPlant(map) != null));
            int itemCount = map.listerThings.AllThings.Count(
                thing => thing.def.category == ThingCategory.Item);
            int filthCount = map.listerThings.AllThings.Count(thing => thing is Filth);
            int buildingCount = map.listerThings.AllThings.Count(thing => thing is Building);
            int powerComponents = powerNets.Sum(net => net.powerComps.Count);
            int batteries = powerNets.Sum(net => net.batteryComps.Count);
            float energyGainPerTick = powerNets.Sum(net => net.CurrentEnergyGainRate());
            float storedEnergy = powerNets.Sum(net => net.CurrentStoredEnergy());

            StringBuilder report = new StringBuilder();
            report.AppendLine("[RimWorldOptim.Poc] Fixture activity report");
            report.AppendLine("Fixture: " + fixtureId);
            report.AppendLine("Tick: " + Find.TickManager.TicksGame.ToString(CultureInfo.InvariantCulture));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Map: {0}x{1}",
                map.Size.x,
                map.Size.z));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Pawns: all={0}, player={1}, freeColonists={2}, colonyAnimals={3}, colonyMechs={4}",
                allPawns.Count,
                playerPawns.Count,
                freeColonists.Count,
                playerPawns.Count(pawn => pawn.IsAnimal),
                playerPawns.Count(pawn => pawn.IsColonyMech)));
            report.AppendLine("Active jobs: " + FormatCounts(
                playerPawns.Where(pawn => pawn.CurJobDef != null),
                pawn => pawn.CurJobDef.defName));
            report.AppendLine("Time assignments: " + FormatCounts(
                freeColonists.Where(pawn => pawn.timetable != null),
                pawn => pawn.timetable.CurrentAssignment.defName));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Work: tables={0}, bills={1}, reservations={2}",
                workTables.Count,
                activeBills,
                map.reservationManager.ReservationsReadOnly.Count));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Topology: buildings={0}, doors={1}, stockpiles={2}, growingZones={3}",
                buildingCount,
                doors.Count,
                stockpiles.Count,
                growingZones.Count));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Growing: cells={0}, plantedCells={1}",
                growingCells,
                plantedGrowingCells));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Map contents: items={0}, filth={1}",
                itemCount,
                filthCount));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Power: nets={0}, traders={1}, batteries={2}, gainPerTick={3:0.###}, stored={4:0.###}",
                powerNets.Count,
                powerComponents,
                batteries,
                energyGainPerTick,
                storedEnergy));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "DLC: Royalty={0}, Ideology={1}, Biotech={2}, Anomaly={3}, Odyssey={4}",
                ModsConfig.RoyaltyActive,
                ModsConfig.IdeologyActive,
                ModsConfig.BiotechActive,
                ModsConfig.AnomalyActive,
                ModsConfig.OdysseyActive));

            Log.Message(report.ToString().TrimEnd());
            Messages.Message(
                "Fixture activity report written to Player.log.",
                MessageTypeDefOf.PositiveEvent,
                false);
        }

        private static string FormatCounts<T>(IEnumerable<T> values, Func<T, string> keySelector)
        {
            string[] counts = values
                .GroupBy(keySelector)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key + "=" + group.Count().ToString(CultureInfo.InvariantCulture))
                .ToArray();

            return counts.Length == 0 ? "none" : string.Join(", ", counts);
        }
    }
}
