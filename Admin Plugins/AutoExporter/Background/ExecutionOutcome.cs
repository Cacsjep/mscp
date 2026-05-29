namespace AutoExporter.Background
{
    /// <summary>
    /// Pure classification of an export run's outcome, isolated from MIP types so the
    /// non-happy combinations (all skipped, some skipped, hard fail) are unit-testable
    /// and shared between the background plugin and the admin view.
    /// </summary>
    internal static class ExecutionOutcome
    {
        public const string Success = "Success";
        public const string Partial = "Partial";
        public const string Skipped = "Skipped";
        public const string Failed  = "Failed";

        /// <param name="success">Did the export itself complete without error.</param>
        /// <param name="cameraCount">Cameras actually exported.</param>
        /// <param name="skippedCount">Cameras dropped for having no recordings.</param>
        public static string Classify(bool success, int cameraCount, int skippedCount)
        {
            if (!success) return Failed;            // the export errored
            if (cameraCount == 0) return Skipped;   // nothing exported (no data anywhere)
            return skippedCount > 0 ? Partial       // exported, but some cameras had no data
                                    : Success;      // everything requested exported
        }
    }
}
