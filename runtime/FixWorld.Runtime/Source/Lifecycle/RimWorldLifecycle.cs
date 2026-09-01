using System;
using FixWorld.Runtime;
using Verse;

namespace FixWorld.Lifecycle
{
    internal enum RimWorldLifecycleEventKind
    {
        PlayDataReady,
        MainMenuReady,
        GameReady,
        GameEnded,
        ShuttingDown
    }

    internal readonly struct RimWorldLifecycleEvent
    {
        internal readonly RimWorldLifecycleEventKind Kind;
        internal readonly int GameGeneration;
        internal readonly Game Game;
        internal readonly string Source;

        internal RimWorldLifecycleEvent(
            RimWorldLifecycleEventKind kind,
            int gameGeneration,
            Game game,
            string source)
        {
            Kind = kind;
            GameGeneration = gameGeneration;
            Game = game;
            Source = source;
        }
    }

    internal static class RimWorldLifecycle
    {
        private static readonly object Sync = new object();

        private static bool playDataReady;
        private static bool mainMenuPublished;
        private static bool shuttingDown;
        private static int gameGeneration;
        private static Game readyGame;
        private static string playDataSource;

        internal static void NotifyPlayDataReady(string source)
        {
            RimWorldLifecycleEvent lifecycleEvent;
            lock (Sync)
            {
                if (playDataReady || shuttingDown)
                {
                    return;
                }

                playDataReady = true;
                playDataSource = string.IsNullOrWhiteSpace(source)
                    ? "play-data"
                    : source;
                lifecycleEvent = CreateEvent(
                    RimWorldLifecycleEventKind.PlayDataReady,
                    null,
                    playDataSource);
            }

            FixWorldEvents.Publish(lifecycleEvent);
        }

        internal static void NotifyMainMenuReady()
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (Sync)
            {
                if (shuttingDown || !playDataReady || mainMenuPublished)
                {
                    return;
                }

                mainMenuPublished = true;
                lifecycleEvent = CreateEvent(
                    RimWorldLifecycleEventKind.MainMenuReady,
                    null,
                    playDataSource + "+main-menu-draw");
            }

            if (lifecycleEvent.HasValue)
            {
                FixWorldEvents.Publish(lifecycleEvent.Value);
            }
        }

        internal static void ObserveFrame()
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (Sync)
            {
                if (shuttingDown)
                {
                    return;
                }

                if (CanPublishGameReady())
                {
                    readyGame = Current.Game;
                    gameGeneration++;
                    lifecycleEvent = CreateEvent(
                        RimWorldLifecycleEventKind.GameReady,
                        readyGame,
                        playDataSource + "+game-ready");
                }
            }

            if (lifecycleEvent.HasValue)
            {
                FixWorldEvents.Publish(lifecycleEvent.Value);
            }
        }

        internal static void NotifyGameEnded(Game game)
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (Sync)
            {
                if (!shuttingDown && ReferenceEquals(readyGame, game))
                {
                    lifecycleEvent = CreateEvent(
                        RimWorldLifecycleEventKind.GameEnded,
                        game,
                        "game-disposed");
                    readyGame = null;
                }
            }

            if (lifecycleEvent.HasValue)
            {
                FixWorldEvents.Publish(lifecycleEvent.Value);
            }
        }

        internal static void NotifyShuttingDown()
        {
            RimWorldLifecycleEvent lifecycleEvent;
            lock (Sync)
            {
                if (shuttingDown)
                {
                    return;
                }

                shuttingDown = true;
                lifecycleEvent = CreateEvent(
                    RimWorldLifecycleEventKind.ShuttingDown,
                    readyGame,
                    "root-shutdown");
            }

            FixWorldEvents.Publish(lifecycleEvent);
        }

        private static bool CanPublishGameReady()
        {
            Game game = Current.Game;
            return playDataReady &&
                   game != null &&
                   !ReferenceEquals(readyGame, game) &&
                   GenScene.InPlayScene &&
                   Current.ProgramState == ProgramState.Playing &&
                   !LongEventHandler.AnyEventNowOrWaiting;
        }

        private static RimWorldLifecycleEvent CreateEvent(
            RimWorldLifecycleEventKind kind,
            Game game,
            string source)
        {
            return new RimWorldLifecycleEvent(
                kind,
                gameGeneration,
                game,
                source ?? kind.ToString());
        }
    }
}
