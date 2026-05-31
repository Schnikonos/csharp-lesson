using System.IO;
using FluentAssertions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Lesson.Tests;

/// <summary>
/// Lesson 23-B: Kubernetes — validates that the k8s manifests and Helm chart
/// are structurally correct without needing a running cluster.
/// </summary>
public class KubernetesManifestTests
{
    // Resolve the repo root relative to the test assembly output directory
    private static readonly string RepoRoot =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

    private static YamlMappingNode LoadYaml(string relativePath)
    {
        var fullPath = Path.Combine(RepoRoot, relativePath);
        File.Exists(fullPath).Should().BeTrue($"manifest file '{relativePath}' should exist");
        using var reader = new StreamReader(fullPath);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return (YamlMappingNode)yaml.Documents[0].RootNode;
    }

    private static string Scalar(YamlMappingNode node, params string[] keys)
    {
        YamlNode current = node;
        foreach (var key in keys)
            current = ((YamlMappingNode)current)[key];
        return ((YamlScalarNode)current).Value!;
    }

    [Fact]
    public void Deployment_HasCorrectApiVersionAndKind()
    {
        var root = LoadYaml("k8s/deployment.yaml");
        Scalar(root, "apiVersion").Should().Be("apps/v1");
        Scalar(root, "kind").Should().Be("Deployment");
    }

    [Fact]
    public void Deployment_HasReplicasAndImage()
    {
        var root = LoadYaml("k8s/deployment.yaml");
        var spec = (YamlMappingNode)root["spec"];
        Scalar(spec, "replicas").Should().Be("2");

        var containers = (YamlSequenceNode)
            ((YamlMappingNode)((YamlMappingNode)spec["template"])["spec"])["containers"];
        var container = (YamlMappingNode)containers[0];
        Scalar(container, "image").Should().Contain("banking-api");
    }

    [Fact]
    public void Deployment_HasLivenessAndReadinessProbes()
    {
        var root = LoadYaml("k8s/deployment.yaml");
        var containers = (YamlSequenceNode)
            ((YamlMappingNode)
                ((YamlMappingNode)
                    ((YamlMappingNode)root["spec"])["template"])["spec"])["containers"];
        var container = (YamlMappingNode)containers[0];
        container.Children.Should().ContainKey(new YamlScalarNode("livenessProbe"));
        container.Children.Should().ContainKey(new YamlScalarNode("readinessProbe"));
    }

    [Fact]
    public void Service_IsClusterIpOnPort80()
    {
        var root = LoadYaml("k8s/service.yaml");
        Scalar(root, "kind").Should().Be("Service");
        var spec = (YamlMappingNode)root["spec"];
        Scalar(spec, "type").Should().Be("ClusterIP");
        var ports = (YamlSequenceNode)spec["ports"];
        Scalar((YamlMappingNode)ports[0], "port").Should().Be("80");
        Scalar((YamlMappingNode)ports[0], "targetPort").Should().Be("8080");
    }

    [Fact]
    public void ConfigMap_ContainsRequiredKeys()
    {
        var root = LoadYaml("k8s/configmap.yaml");
        Scalar(root, "kind").Should().Be("ConfigMap");
        var data = (YamlMappingNode)root["data"];
        data.Children.Should().ContainKey(new YamlScalarNode("ASPNETCORE_ENVIRONMENT"));
        data.Children.Should().ContainKey(new YamlScalarNode("AllowedHosts"));
    }

    [Fact]
    public void Ingress_RoutesToBankingLocal()
    {
        var root = LoadYaml("k8s/ingress.yaml");
        Scalar(root, "kind").Should().Be("Ingress");
        var rules = (YamlSequenceNode)((YamlMappingNode)root["spec"])["rules"];
        Scalar((YamlMappingNode)rules[0], "host").Should().Be("banking.local");
    }

    [Fact]
    public void HelmChart_HasCorrectMetadata()
    {
        var root = LoadYaml("helm/banking-api/Chart.yaml");
        Scalar(root, "name").Should().Be("banking-api");
        Scalar(root, "apiVersion").Should().Be("v2");
        root.Children.Should().ContainKey(new YamlScalarNode("version"));
    }

    [Fact]
    public void HelmValues_HasReplicaCountAndImage()
    {
        var root = LoadYaml("helm/banking-api/values.yaml");
        Scalar(root, "replicaCount").Should().Be("2");
        var image = (YamlMappingNode)root["image"];
        Scalar(image, "repository").Should().Be("banking-api");
    }
}
