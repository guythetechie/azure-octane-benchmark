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

using static LanguageExt.Prelude;

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

public delegate Aff<Unit> QueueVirtualMachineCreation(Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken);

public delegate Aff<Unit> CreateVirtualMachine(VirtualMachine virtualMachine, CancellationToken cancellationToken);

public delegate Aff<Unit> QueueOctaneBenchmark(VirtualMachine virtualMachine, CancellationToken cancellationToken);

public delegate Aff<Unit> RunOctaneBenchmark(VirtualMachine virtualMachine, DiagnosticId diagnosticId, CancellationToken cancellationToken);

public delegate Aff<Unit> QueueVirtualMachineDeletion(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public delegate Aff<Unit> DeleteVirtualMachine(VirtualMachineName virtualMachineName, CancellationToken cancellationToken);

public static class VirtualMachineModule
{
    public static Aff<Unit> CreateVirtualMachine(ResourceGroupResource resourceGroup, SubnetData subnetData, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        async ValueTask<NetworkInterfaceResource> createNetworkInterface()
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

        async ValueTask<Unit> createVirtualMachine(NetworkInterfaceResource networkInterface)
        {
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
                               .CreateOrUpdateAsync(WaitUntil.Completed, virtualMachine.Name, virtualMachineData, cancellationToken);

            return unit;
        };

        return Aff(createNetworkInterface).MapAsync(createVirtualMachine);
    }

    public static Aff<Unit> RunOctaneBenchmark(Base64Script base64Script, BenchmarkExecutableUri benchmarkUri, DiagnosticId diagnosticId, ApplicationInsightsConnectionString applicationInsightsConnectionString, ResourceGroupResource resourceGroup, VirtualMachine virtualMachine, CancellationToken cancellationToken)
    {
        async ValueTask<VirtualMachineResource> getVirtualMachineResource()
        {
            return await resourceGroup.GetVirtualMachineAsync(virtualMachine.Name, cancellationToken: cancellationToken)
                                      .Map(response => response.Value);
        }

        async ValueTask<Unit> runCommand(VirtualMachineResource virtualMachineResource)
        {
            var input = new RunCommandInput("RunPowerShellScript")
            {
                Parameters =
                {
                    new RunCommandInputParameter("BenchmarkDownloadUri", benchmarkUri),
                    new RunCommandInputParameter("DiagnosticId", diagnosticId),
                    new RunCommandInputParameter("VirtualMachineSku", virtualMachine.Sku),
                    new RunCommandInputParameter("ApplicationInsightsConnectionString", applicationInsightsConnectionString)
                },
                Script =
                {
                    Encoding.UTF8.GetString(Convert.FromBase64String(base64Script))
                }
            };

            await virtualMachineResource.RunCommandAsync(WaitUntil.Completed, input, cancellationToken);

            return unit;
        }

        return Aff(getVirtualMachineResource).MapAsync(runCommand);
    }

    public static Aff<Unit> DeleteVirtualMachine(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        async ValueTask<VirtualMachineResource> getVirtualMachine()
        {
            return await resourceGroup.GetVirtualMachineAsync(virtualMachineName, cancellationToken: cancellationToken)
                                      .Map(response => response.Value);
        }

        async ValueTask<Unit> deleteVirtualMachine(VirtualMachineResource virtualMachineResource)
        {
            await virtualMachineResource.DeleteAsync(WaitUntil.Completed, forceDeletion: true, cancellationToken);

            return unit;
        }

        async ValueTask<Unit> deleteDisk(VirtualMachineResource virtualMachineResource)
        {
            var osDiskName = virtualMachineResource.Data.StorageProfile.OSDisk.Name;

            await resourceGroup.GetDiskAsync(osDiskName, cancellationToken: cancellationToken)
                               .Bind(osDiskResponse => osDiskResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken));

            return unit;
        }

        async ValueTask<Unit> deleteNetworkInterface(VirtualMachineResource virtualMachineResource)
        {
            var networkInterfaceName = virtualMachineResource.Data.NetworkProfile.NetworkInterfaces.First().Id
                                                                                                   .Split('/')
                                                                                                   .Last();

            await resourceGroup.GetNetworkInterfaceAsync(networkInterfaceName, cancellationToken: cancellationToken)
                               .Bind(networkInterfaceResponse => networkInterfaceResponse.Value.DeleteAsync(WaitUntil.Started, cancellationToken));

            return unit;
        }

        return Aff(getVirtualMachine).Do(deleteVirtualMachine)
                                     .Do(deleteDisk)
                                     .Do(deleteNetworkInterface)
                                     .ToUnit();
    }
}