namespace TimeWave.Core
{
    /// <summary>Tunables of the time mechanics; names follow Achron's Scenario* skin variables where one exists.</summary>
    public sealed class ScenarioConfig
    {
        public int MapWidth = 48;
        public int MapHeight = 32;
        public int PlayerCount = 2;

        public int TicksPerSecond = 10;

        /// <summary>Length of the editable time window (Achron: ScenarioTimeWindowSizeInSeconds).</summary>
        public int WindowSeconds = 300;
        /// <summary>Where the present sits inside the window once the game has run long enough (ScenarioPresentTimeInSeconds).</summary>
        public int PresentOffsetSeconds = 180;

        /// <summary>A full world snapshot is kept every this many ticks; seeks replay at most this many ticks.</summary>
        public int KeyframeInterval = 10;

        /// <summary>Number of background time waves (ScenarioNumTimeWaves) and their speed relative to time (ScenarioTimeWaveRate, > 1).</summary>
        /// <remarks>
        /// A change in the past reaches the present within about WindowTicks / (BackgroundWaves x rate) of real time,
        /// plus the catch-up distance / (rate - 1). 4 waves at 4x on a 300 s window: under ~20 s.
        /// </remarks>
        public int BackgroundWaves = 4;
        public double BackgroundWaveRate = 4.0;

        public float ChronoMax = 100f;
        public float ChronoInitial = 60f;
        public float PresentGainPerSecond = 2f;      // ScenarioPresentCERateGain
        public float PastDrainPerSecond = 1f;        // ScenarioPastCERateDrain
        public float FutureDrainPerSecond = 1.5f;    // ScenarioFutureCERateDrain
        public float StoppedDrainPerSecond = 2f;     // ScenarioStoppedCERateDrain
        public float PastCommandCost = 5f;           // ScenarioPastCEEventDrain

        /// <summary>Jumps closer than this to the present just return to the present.</summary>
        public int PresentSnapTicks = 10;

        public int StatsBucketTicks = 30;

        /// <summary>Upper bound on simulation steps per engine update, so a slow machine degrades instead of freezing.</summary>
        public int MaxStepsPerUpdate = 20000;

        /// <summary>Ticks between orders of the built-in opponent AI.</summary>
        public int AiDecisionTicks = 250;

        public int WindowTicks => WindowSeconds * TicksPerSecond;
        public int PresentOffsetTicks => PresentOffsetSeconds * TicksPerSecond;
    }
}
