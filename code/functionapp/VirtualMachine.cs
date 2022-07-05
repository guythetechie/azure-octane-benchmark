using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using common;
using LanguageExt;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using static LanguageExt.Prelude;

namespace functionapp;

public record BenchmarkExecutableUri : UriRecord
{
    public BenchmarkExecutableUri(string value) : base(value) { }
}

public record ApplicationInsightsConnectionString
{
    public ApplicationInsightsConnectionString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Application insights connection string cannot be null or whitespace.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

public record BenchmarkScript(BinaryData Data)
{
    public string Utf8String => Data.ToString();
}

public static class VirtualMachineModule
{
    public static async ValueTask CreateVirtualMachine(ResourceGroupResource resourceGroup, SubnetData subnetData, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        var networkInterface = await CreateNetworkInterface(resourceGroup, subnetData, virtualMachine, cancellationToken);

        await CreateVirtualMachine(networkInterface, resourceGroup, virtualMachine, cancellationToken);
    }

    public static async ValueTask RunOctaneBenchmark(BenchmarkScript benchmarkScript, BenchmarkExecutableUri benchmarkUri, DiagnosticId diagnosticId, ApplicationInsightsConnectionString applicationInsightsConnectionString, ResourceGroupResource resourceGroup, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        var virtualMachineResource = await GetVirtualMachineResource(resourceGroup, virtualMachine.Name, cancellationToken);

        await RunCommand(virtualMachineResource, benchmarkUri, diagnosticId, applicationInsightsConnectionString, virtualMachine, benchmarkScript, cancellationToken);
    }

    public static async ValueTask DeleteVirtualMachine(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var virtualMachineResource = await GetVirtualMachineResource(resourceGroup, virtualMachineName, cancellationToken);
        await virtualMachineResource.DeleteAsync(WaitUntil.Completed, forceDeletion: true, cancellationToken);

        var osDiskName = virtualMachineResource.Data.StorageProfile.OSDisk.Name;
        var osDiskResponse = await resourceGroup.GetDiskAsync(osDiskName, cancellationToken: cancellationToken);
        await osDiskResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken);

        var tasks = virtualMachineResource.Data
                                          .NetworkProfile
                                          .NetworkInterfaces
                                          .Select(reference => reference.Id.Split('/').Last())
                                          .Select(networkInterfaceName => from networkInterfaceResponse in resourceGroup.GetNetworkInterfaceAsync(networkInterfaceName, cancellationToken: cancellationToken)
                                                                          from operation in networkInterfaceResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken)
                                                                          select operation);

        await Task.WhenAll(tasks);
    }

    private static async ValueTask<NetworkInterfaceResource> CreateNetworkInterface(ResourceGroupResource resourceGroup, SubnetData subnetData, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        var nicData = new NetworkInterfaceData()
        {
            Location = resourceGroup.Data.Location,
            IPConfigurations =
                {
                    new NetworkInterfaceIPConfigurationData
                    {
                        Name = "ip-configuration",
                        PrivateIPAllocationMethod = IPAllocationMethod.Dynamic,
                        Subnet = subnetData
                    }
                }
        };

        return await resourceGroup.GetNetworkInterfaces()
                                  .CreateOrUpdateAsync(WaitUntil.Completed, $"{virtualMachine.Name}-nic", nicData, cancellationToken)
                                  .Map(operation => operation.Value);
    }

    private static async ValueTask CreateVirtualMachine(NetworkInterfaceResource networkInterface, ResourceGroupResource resourceGroup, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        var virtualMachineData = new VirtualMachineData(resourceGroup.Data.Location)
        {
            HardwareProfile = new HardwareProfile
            {
                VmSize = new VirtualMachineSizeTypes(virtualMachine.Sku.Value)
            },
            LicenseType = "Windows_Client",
            OSProfile = new OSProfile
            {
                AdminUsername = "octaneadmin",
                AdminPassword = "@c@mdin212345A",
                ComputerName = virtualMachine.Name.Value,
            },
            NetworkProfile = new NetworkProfile
            {
                NetworkInterfaces =
                    {
                        new NetworkInterfaceReference
                        {
                            Primary= true,
                            Id = networkInterface.Id
                        }
                    }
            },
            StorageProfile = new StorageProfile
            {
                OSDisk = new OSDisk(DiskCreateOptionTypes.FromImage)
                {
                    OSType = OperatingSystemTypes.Windows,
                    Name = $"{virtualMachine.Name}-osdisk",
                    ManagedDisk = new ManagedDiskParameters
                    {
                        StorageAccountType = StorageAccountTypes.StandardLRS
                    }
                },
                ImageReference = new ImageReference
                {
                    Publisher = "MicrosoftWindowsDesktop",
                    Offer = "windows-11",
                    Sku = "win11-21h2-avd",
                    Version = "latest"
                }
            }
        };

        await resourceGroup.GetVirtualMachines()
                           .CreateOrUpdateAsync(WaitUntil.Completed, virtualMachine.Name.Value, virtualMachineData, cancellationToken);
    }

    private static async ValueTask<VirtualMachineResource> GetVirtualMachineResource(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        return await resourceGroup.GetVirtualMachineAsync(virtualMachineName.Value, cancellationToken: cancellationToken)
                                  .Map(response => response.Value);
    }

    private static async ValueTask RunCommand(VirtualMachineResource virtualMachineResource, BenchmarkExecutableUri benchmarkUri, DiagnosticId diagnosticId, ApplicationInsightsConnectionString applicationInsightsConnectionString, VirtualMachine virtualMachine, BenchmarkScript benchmarkScript, CancellationToken cancellationToken)
    {
        var input = new RunCommandInput("RunPowerShellScript")
        {
            Parameters =
                {
                    new RunCommandInputParameter("BenchmarkDownloadUri", benchmarkUri.Value),
                    new RunCommandInputParameter("DiagnosticId", diagnosticId.Value),
                    new RunCommandInputParameter("VirtualMachineSku", virtualMachine.Sku.Value),
                    new RunCommandInputParameter("ApplicationInsightsConnectionString", applicationInsightsConnectionString.Value)
                },
            Script =
                {
                    benchmarkScript.Utf8String
                }
        };

        await virtualMachineResource.RunCommandAsync(WaitUntil.Completed, input, cancellationToken);
    }
}