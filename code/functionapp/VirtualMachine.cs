using Azure;
using Azure.ResourceManager;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using LanguageExt;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace functionapp;

public record VirtualMachineSku : NonEmptyString
{
    public VirtualMachineSku(string value) : base(value) { }
}

public record VirtualMachineName : NonEmptyString
{
    public VirtualMachineName(string value) : base(value) { }

}

public record VirtualMachine(VirtualMachineName Name, VirtualMachineSku Sku);

public delegate ValueTask<Unit> QueueVirtualMachineCreation(Seq<(VirtualMachine VirtualMachine, DateTimeOffset EnqueueAt)> virtualMachines, CancellationToken cancellationToken);

public delegate ValueTask<Unit> CreateVirtualMachine(VirtualMachine virtualMachine, CancellationToken cancellationToken);

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

        await resourceGroup.GetNetworkInterfaces().CreateOrUpdateAsync(WaitUntil.Completed, $"{virtualMachine.Name}-nic", nicData, cancellationToken);

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
                        Primary= true
                    }
                }
            },
            StorageProfile = new StorageProfile
            {
                OSDisk = new OSDisk(DiskCreateOptionTypes.FromImage)
                {
                    OSType = OperatingSystemTypes.Windows,
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

    public static async ValueTask<Unit> DeleteVirtualMachine(ResourceGroupResource resourceGroup, VirtualMachineName virtualMachineName, CancellationToken cancellationToken)
    {
        var virtualMachineResponse = await resourceGroup.GetVirtualMachines().GetAsync(virtualMachineName, cancellationToken: cancellationToken);

        await virtualMachineResponse.Value.DeleteAsync(WaitUntil.Started, forceDeletion: true, cancellationToken);

        return Unit.Default;
    }
}