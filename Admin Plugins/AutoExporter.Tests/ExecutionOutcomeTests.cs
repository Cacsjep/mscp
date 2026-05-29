using AutoExporter.Background;
using Xunit;

namespace AutoExporter.Tests
{
    public class ExecutionOutcomeTests
    {
        [Fact]
        public void Failed_when_not_successful_regardless_of_counts()
        {
            Assert.Equal("Failed", ExecutionOutcome.Classify(false, 0, 0));
            Assert.Equal("Failed", ExecutionOutcome.Classify(false, 5, 0));
            Assert.Equal("Failed", ExecutionOutcome.Classify(false, 5, 2));
        }

        [Fact]
        public void Skipped_when_successful_but_nothing_exported()
        {
            Assert.Equal("Skipped", ExecutionOutcome.Classify(true, 0, 0));
            Assert.Equal("Skipped", ExecutionOutcome.Classify(true, 0, 3));
        }

        [Fact]
        public void Partial_when_some_exported_and_some_skipped()
        {
            Assert.Equal("Partial", ExecutionOutcome.Classify(true, 3, 1));
            Assert.Equal("Partial", ExecutionOutcome.Classify(true, 1, 10));
        }

        [Fact]
        public void Success_when_everything_exported_and_none_skipped()
        {
            Assert.Equal("Success", ExecutionOutcome.Classify(true, 4, 0));
            Assert.Equal("Success", ExecutionOutcome.Classify(true, 1, 0));
        }
    }
}
