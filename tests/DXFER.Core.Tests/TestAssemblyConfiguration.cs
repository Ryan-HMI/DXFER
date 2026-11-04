// Native-runtime tests share an eight-process budget. Leave room for fixtures
// that use more than one runtime without making results depend on CPU count.
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 4)]
