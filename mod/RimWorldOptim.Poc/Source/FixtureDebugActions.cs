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

        [DebugAction(Category, "Report fixture activity",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ReportFixtureActivity()
        {
            WriteActivityReport(Find.CurrentMap, "current-map");
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
            List<Zone_Fishing> fishingZones = map.zoneManager.AllZones
                .OfType<Zone_Fishing>()
                .ToList();
            List<PowerNet> powerNets = map.powerNetManager.AllNetsListForReading;

            int activeBills = workTables.Sum(table => table.BillStack.Count);
            int suspendedBills = workTables.Sum(table =>
                table.BillStack.Bills.Count(bill => bill.suspended));
            int growingCells = growingZones.Sum(zone => zone.Cells.Count);
            int plantedGrowingCells = growingZones.Sum(zone =>
                zone.Cells.Count(cell => cell.GetPlant(map) != null));
            int harvestablePlants = growingZones.Sum(zone =>
                zone.Cells.Count(cell => cell.GetPlant(map)?.HarvestableNow == true));
            int fishableCells = fishingZones.Sum(zone => zone.FishbleCells.Count);
            int itemCount = map.listerThings.AllThings.Count(
                thing => thing.def.category == ThingCategory.Item);
            int filthCount = map.listerThings.AllThings.Count(thing => thing is Filth);
            int buildingCount = map.listerThings.AllThings.Count(thing => thing is Building);
            int damagedBuildings = map.listerThings.AllThings.Count(thing =>
                thing is Building building && building.HitPoints < building.MaxHitPoints);
            int plannedConstruction = map.listerThings.AllThings.Count(thing =>
                thing is Blueprint || thing is Frame);
            int powerComponents = powerNets.Sum(net => net.powerComps.Count);
            int batteries = powerNets.Sum(net => net.batteryComps.Count);
            int poweredConsumers = powerNets.Sum(net =>
                net.powerComps.OfType<CompPowerTrader>().Count(comp => comp.PowerOn));
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
                "Work: tables={0}, bills={1}, suspendedBills={2}, reservations={3}",
                workTables.Count,
                activeBills,
                suspendedBills,
                map.reservationManager.ReservationsReadOnly.Count));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Topology: buildings={0}, damagedBuildings={1}, plannedConstruction={2}, doors={3}, stockpiles={4}, growingZones={5}, fishingZones={6}",
                buildingCount,
                damagedBuildings,
                plannedConstruction,
                doors.Count,
                stockpiles.Count,
                growingZones.Count,
                fishingZones.Count));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Growing: cells={0}, plantedCells={1}, harvestablePlants={2}",
                growingCells,
                plantedGrowingCells,
                harvestablePlants));
            report.AppendLine("Fishing: fishableCells=" +
                              fishableCells.ToString(CultureInfo.InvariantCulture));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Map contents: items={0}, filth={1}",
                itemCount,
                filthCount));
            report.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "Power: nets={0}, traders={1}, poweredConsumers={2}, batteries={3}, gainPerTick={4:0.###}, stored={5:0.###}",
                powerNets.Count,
                powerComponents,
                poweredConsumers,
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
