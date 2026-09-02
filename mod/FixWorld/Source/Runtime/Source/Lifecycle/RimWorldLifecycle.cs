using System;
using FixWorld.Events;
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

    internal sealed class RimWorldLifecycle
    {
        private readonly object sync = new object();
        private readonly EventBus events;

        private bool playDataReady;
        private bool mainMenuPublished;
        private bool shuttingDown;
        private int gameGeneration;
        private Game readyGame;
        private string playDataSource;

        internal RimWorldLifecycle(EventBus events)
        {
            this.events = events ?? throw new ArgumentNullException(nameof(events));
        }

        internal void NotifyPlayDataReady(string source)
        {
            RimWorldLifecycleEvent lifecycleEvent;
            lock (sync)
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

            events.Publish(lifecycleEvent);
        }

        internal void NotifyMainMenuReady()
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (sync)
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
                events.Publish(lifecycleEvent.Value);
            }
        }

        internal void ObserveFrame()
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (sync)
            {
                if (shuttingDown)
                {
                    return;
                }

                if (CanPublishGameReady())
                {
                    readyGame = Current.Game;
                    mainMenuPublished = false;
                    gameGeneration++;
                    lifecycleEvent = CreateEvent(
                        RimWorldLifecycleEventKind.GameReady,
                        readyGame,
                        playDataSource + "+game-ready");
                }
            }

            if (lifecycleEvent.HasValue)
            {
                events.Publish(lifecycleEvent.Value);
            }
        }

        internal void NotifyGameEnded(Game game)
        {
            RimWorldLifecycleEvent? lifecycleEvent = null;
            lock (sync)
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
                events.Publish(lifecycleEvent.Value);
            }
        }

        internal void NotifyShuttingDown()
        {
            RimWorldLifecycleEvent lifecycleEvent;
            lock (sync)
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

            events.Publish(lifecycleEvent);
        }

        private bool CanPublishGameReady()
        {
            Game game = Current.Game;
            return playDataReady &&
                   game != null &&
                   !ReferenceEquals(readyGame, game) &&
                   GenScene.InPlayScene &&
                   Current.ProgramState == ProgramState.Playing &&
                   !LongEventHandler.AnyEventNowOrWaiting;
        }

        private RimWorldLifecycleEvent CreateEvent(
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
