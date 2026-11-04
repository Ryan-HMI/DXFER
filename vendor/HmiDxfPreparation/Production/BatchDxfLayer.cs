namespace HMISync.Web.Production;

public sealed record BatchDxfLayer(string Name, int EntityCount, bool Automatic, bool RequiresReview, bool Removed);
