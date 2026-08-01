using System.Runtime.CompilerServices;

// Expose internals to the test project so pure logic (CleanFieldName,
// JSON serialization, model defaults) can be unit-tested without a live process.
[assembly: InternalsVisibleTo("SboxDumper.Tests")]
