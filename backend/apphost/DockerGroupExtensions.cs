using Aspire.Hosting.ApplicationModel;

internal static class DockerGroupExtensions
{
    // com.docker.compose.project is what Docker Desktop reads to collapse
    // containers into a single named row. It works on any container, not
    // just compose-managed ones — Aspire just needs to forward it via
    // `docker run --label`.
    public static IResourceBuilder<T> WithDockerGroup<T>(this IResourceBuilder<T> builder, string project)
        where T : ContainerResource
        => builder.WithContainerRuntimeArgs("--label", $"com.docker.compose.project={project}");
}
