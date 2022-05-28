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
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;


public record BenchmarkExecutableUri : UriRecord
{
    public BenchmarkExecutableUri(string value) : base(value) { }
}

public record ApplicationInsightsConnectionString : NonEmptyString
{
    public ApplicationInsightsConnectionString(string value) : base(value) { }
}

public record Base64Script : NonEmptyString
{
    public Base64Script(string value) : base(value) { }
}

public delegate ValueTask<Unit> QueueVirtualMachineCreation(Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken);

public delegate ValueTask<Unit> CreateVirtualMachine(VirtualMachine virtualMachine, CancellationToken cancellationToken);

public delegate ValueTask<Unit> QueueOctaneBenchmark(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public delegate ValueTask<Unit> RunOctaneBenchmark(VirtualMachineName virtualMachineName, DiagnosticId diagnosticId, CancellationToken cancellationToken);

public delegate ValueTask<Unit> QueueVirtualMachineDeletion(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public delegate ValueTask<Unit> DeleteVirtualMachine(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public static class VirtualMachineModule
{
    public static async ValueTask<Unit> CreateVirtualMachine(ResourceGroupResource resourceGroup, SubnetData subnetData, VirtualMachine virtualMachine, CancellationToken cancellationToken)
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

        var nicOperation = await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, $"{virtualMachine.Name}-nic", nicData, cancellationToken);

        var virtualMachineData = new VirtualMachineData(resourceGroup.Data.Location)
        {
            HardwareProfile = new HardwareProfile
            {
                VmSize = new VirtualMachineSizeTypes(virtualMachine.Sku)
            },
            LicenseType = "Windows_Client",
            OSProfile = new OSProfile
            {
                AdminUsername = "octaneadmin",
                AdminPassword = "@c@mdin212345A",
                ComputerName = virtualMachine.Name,
            },
            NetworkProfile = new NetworkProfile
            {
                NetworkInterfaces =
                {
                    new NetworkInterfaceReference
                    {
                        Primary= true,
                        Id = nicOperation.Value.Id
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

        await resourceGroup.GetVirtualMachines().CreateOrUpdateAsync(WaitUntil.Completed, virtualMachine.Name, virtualMachineData, cancellationToken);

        return Unit.Default;
    }

    public static async ValueTask<Unit> RunOctaneBenchmark(Base64Script base64Script, BenchmarkExecutableUri benchmarkUri, DiagnosticId diagnosticId, ApplicationInsightsConnectionString applicationInsightsConnectionString, ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var virtualMachineResponse = await resourceGroup.GetVirtualMachineAsync(virtualMachineName, cancellationToken: cancellationToken);
        var virtualMachine = virtualMachineResponse.Value;

        var input = new RunCommandInput("RunPowerShellScript")
        {
            Parameters =
            {
                new RunCommandInputParameter("BenchmarkDownloadUri", benchmarkUri),
                new RunCommandInputParameter("DiagnosticId", diagnosticId),
                new RunCommandInputParameter("ApplicationInsightsConnectionString", applicationInsightsConnectionString)
            },
            Script =
            {
                Encoding.UTF8.GetString(Convert.FromBase64String(base64Script))
            }
        };

        await virtualMachine.RunCommandAsync(WaitUntil.Completed, input, cancellationToken);

        return Unit.Default;
    }

    public static async ValueTask<Unit> DeleteVirtualMachine(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var virtualMachineResponse = await resourceGroup.GetVirtualMachineAsync(virtualMachineName, cancellationToken: cancellationToken);
        await virtualMachineResponse.Value.DeleteAsync(WaitUntil.Completed, forceDeletion: true, cancellationToken);

        var osDiskName = virtualMachineResponse.Value.Data.StorageProfile.OSDisk.Name;
        var osDiskResponse = await resourceGroup.GetDiskAsync(osDiskName, cancellationToken: cancellationToken);
        await osDiskResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken);

        var networkInterfaceName = virtualMachineResponse.Value.Data.NetworkProfile.NetworkInterfaces.First().Id.Split('/').Last();
        var networkInterfaceResponse = await resourceGroup.GetNetworkInterfaceAsync(networkInterfaceName, cancellationToken: cancellationToken);
        await networkInterfaceResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken);

        return Unit.Default;
    }

    internal static Task<ValueTask<Unit>> RunOctaneBenchmark(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}