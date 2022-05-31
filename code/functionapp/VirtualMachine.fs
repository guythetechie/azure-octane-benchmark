namespace functionapp

open Azure
open Azure.ResourceManager.Compute
open Azure.ResourceManager.Compute.Models
open Azure.ResourceManager.Network
open Azure.ResourceManager.Network.Models
open Azure.ResourceManager.Resources
open FSharpPlus
open System
open System.Text.Json.Nodes

open common

type NetworkInterfaceName =
    private
    | NetworkInterfaceName of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Network interface name cannot be empty."
        else
            value
            |> NonEmptyString.fromString
            |> NetworkInterfaceName

    static member toString(NetworkInterfaceName nonEmptyString) = NonEmptyString.toString nonEmptyString

[<RequireQualifiedAccess>]
module NetworkInterface =
    let get (resourceGroup: ResourceGroupResource) networkInterfaceName =
        async {
            let! cancellationToken = Async.CancellationToken

            return!
                resourceGroup.GetNetworkInterfaceAsync(
                    NetworkInterfaceName.toString networkInterfaceName,
                    cancellationToken = cancellationToken
                )
                |> Async.AwaitTask
                |> Async.map (fun response -> response.Value)
        }

    let create (resourceGroup: ResourceGroupResource) data networkInterfaceName =
        async {
            let! cancellationToken = Async.CancellationToken

            return!
                resourceGroup
                    .GetNetworkInterfaces()
                    .CreateOrUpdateAsync(
                        WaitUntil.Completed,
                        NetworkInterfaceName.toString networkInterfaceName,
                        data,
                        cancellationToken
                    )
                |> Task.map (fun operation -> operation.Value)
                |> Async.AwaitTask
        }

    let deleteByResource waitUntil (networkInterface: NetworkInterfaceResource) =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                networkInterface.DeleteAsync(waitUntil, cancellationToken)
                |> Async.AwaitTask
                |> Async.Ignore
        }

    let deleteByName waitUntil resourceGroup networkInterfaceName =
        get resourceGroup networkInterfaceName
        |> Async.bind (deleteByResource waitUntil)

type DiskName =
    private
    | DiskName of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Disk name cannot be empty."
        else
            value |> NonEmptyString.fromString |> DiskName

    static member toString(DiskName nonEmptyString) = NonEmptyString.toString nonEmptyString

type SubnetId =
    private
    | SubnetId of NonEmptyString

    static member fromString value =
        if String.IsNullOrWhiteSpace(value) then
            invalidOp "Subnet ID cannot be empty."
        else
            value |> NonEmptyString.fromString |> SubnetId

    static member toString(SubnetId nonEmptyString) = NonEmptyString.toString nonEmptyString

[<RequireQualifiedAccess>]
module Disk =
    let get (resourceGroup: ResourceGroupResource) diskName =
        async {
            let! cancellationToken = Async.CancellationToken

            return!
                resourceGroup.GetDiskAsync(DiskName.toString diskName, cancellationToken = cancellationToken)
                |> Async.AwaitTask
                |> Async.map (fun response -> response.Value)
        }

    let deleteByResource waitUntil (disk: DiskResource) =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                disk.DeleteAsync(waitUntil, cancellationToken)
                |> Async.AwaitTask
                |> Async.Ignore
        }

    let deleteByName waitUntil resourceGroup diskName =
        get resourceGroup diskName
        |> Async.bind (deleteByResource waitUntil)


[<RequireQualifiedAccess>]
module VirtualMachine =
    let get (resourceGroup: ResourceGroupResource) virtualMachineName =
        async {
            let! cancellationToken = Async.CancellationToken

            return!
                resourceGroup.GetVirtualMachineAsync(
                    VirtualMachineName.toString virtualMachineName,
                    cancellationToken = cancellationToken
                )
                |> Async.AwaitTask
                |> Async.map (fun response -> response.Value)
        }

    let private createNetworkInterface (resourceGroup: ResourceGroupResource) subnetId networkInterfaceName =
        async {
            let networkInterfaceData =
                let interfaceData = new NetworkInterfaceData()
                interfaceData.Location <- resourceGroup.Data.Location

                interfaceData.IPConfigurations.Add(
                    let ipConfiguration =
                        new NetworkInterfaceIPConfigurationData()

                    ipConfiguration.Name <- "ip-configuration"
                    ipConfiguration.PrivateIPAllocationMethod <- IPAllocationMethod.Dynamic

                    ipConfiguration.Subnet <-
                        let subnet = SubnetData()
                        subnet.Id <- SubnetId.toString subnetId
                        subnet

                    ipConfiguration
                )

                interfaceData

            return! NetworkInterface.create resourceGroup networkInterfaceData networkInterfaceName
        }

    let create resourceGroup subnetId (virtualMachine: VirtualMachine) =
        async {
            let networkInterfaceName =
                let virtualMachineNameString =
                    VirtualMachineName.toString virtualMachine.Name

                NetworkInterfaceName.fromString $"{virtualMachineNameString}-nic"

            let! networkInterface = createNetworkInterface resourceGroup subnetId networkInterfaceName

            let virtualMachineData =
                let data =
                    new VirtualMachineData(resourceGroup.Data.Location)

                data.HardwareProfile <-
                    let profile = new HardwareProfile()
                    profile.VmSize <- new VirtualMachineSizeTypes(VirtualMachineSku.toString virtualMachine.Sku)
                    profile

                data.LicenseType <- "Windows_Client"

                data.OSProfile <-
                    let profile = new OSProfile()
                    profile.AdminUsername <- "octaneadmin"
                    profile.AdminPassword <- "@c@mdin212345A"
                    profile.ComputerName <- VirtualMachineName.toString virtualMachine.Name
                    profile

                data.NetworkProfile <-
                    let profile = new NetworkProfile()

                    profile.NetworkInterfaces.Add(
                        let reference = new NetworkInterfaceReference()
                        reference.Primary <- true
                        reference.Id <- networkInterface.Id
                        reference
                    )

                    profile

                data.StorageProfile <-
                    let profile = new StorageProfile()

                    profile.OSDisk <-
                        let osDisk =
                            new OSDisk(DiskCreateOptionTypes.FromImage)

                        osDisk.OSType <- OperatingSystemTypes.Windows
                        osDisk.Name <- $"{VirtualMachineName.toString virtualMachine.Name}-osdisk"

                        osDisk.ManagedDisk <-
                            let parameters = new ManagedDiskParameters()
                            parameters.StorageAccountType <- StorageAccountTypes.StandardLRS
                            parameters

                        osDisk

                    profile.ImageReference <-
                        let reference = new ImageReference()
                        reference.Publisher <- "MicrosoftWindowsDesktop"
                        reference.Offer <- "windows-11"
                        reference.Sku <- "win11-21h2-avd"
                        reference.Version <- "latest"
                        reference

                    profile

                data.BootDiagnostics <-
                    let diagnostics = new BootDiagnostics()
                    diagnostics.Enabled <- true
                    diagnostics

                data

            let! cancellationToken = Async.CancellationToken

            do!
                resourceGroup
                    .GetVirtualMachines()
                    .CreateOrUpdateAsync(
                        WaitUntil.Completed,
                        virtualMachineData.OSProfile.ComputerName,
                        virtualMachineData,
                        cancellationToken
                    )
                |> Async.AwaitTask
                |> Async.Ignore
        }

    let runCustomScript (scriptUri: Uri) scriptParameters (virtualMachine: VirtualMachineResource) =
        async {
            let data =
                let data =
                    new VirtualMachineExtensionData(virtualMachine.Data.Location)

                data.AutoUpgradeMinorVersion <- true

                data.Settings <-
                    let commandToExecute =
                        let scriptFileName = Array.last scriptUri.Segments

                        let parameters =
                            scriptParameters
                            |> Map.toSeq
                            |> Seq.map (fun (key, value) -> $"-{key} \"{value}\"")
                            |> String.concat " "

                        $"powershell -NonInteractive -ExecutionPolicy Unrestricted -File {scriptFileName} {parameters}"

                    let fileUris =
                        new JsonArray()
                        |> JsonArray.addStringProperty (scriptUri.ToString())

                    new JsonObject()
                    |> JsonObject.addStringProperty "commandToExecute" commandToExecute
                    |> JsonObject.addProperty "fileUris" fileUris
                    |> JsonObject.toBytes
                    |> BinaryData

                data.Publisher <- "Microsoft.Compute"
                data.TypePropertiesType <- "CustomScriptExtension"
                data.TypeHandlerVersion <- "1.10"
                data

            let! cancellationToken = Async.CancellationToken

            do!
                virtualMachine
                    .GetVirtualMachineExtensions()
                    .CreateOrUpdateAsync(WaitUntil.Completed, "CustomScriptExtension", data, cancellationToken)
                |> Async.AwaitTask
                |> Async.Ignore
        }

    let private deleteNetworkInterfaces waitUntil resourceGroup (virtualMachine: VirtualMachineResource) =
        async {
            do!
                virtualMachine.Data.NetworkProfile.NetworkInterfaces
                |> Seq.map (fun networkInterface ->
                    networkInterface.Id.Split('/')
                    |> Array.last
                    |> NetworkInterfaceName.fromString)
                |> Seq.map (fun networkInterfaceName ->
                    NetworkInterface.deleteByName waitUntil resourceGroup networkInterfaceName)
                |> Async.Parallel
                |> Async.Ignore
        }

    let private deleteDisks waitUntil resourceGroup (virtualMachine: VirtualMachineResource) =
        async {
            do!
                virtualMachine.Data.StorageProfile.DataDisks
                |> Seq.map (fun disk -> disk.Name)
                |> Seq.append [ virtualMachine.Data.StorageProfile.OSDisk.Name ]
                |> Seq.map (fun diskName -> DiskName.fromString diskName)
                |> Seq.map (fun diskName -> Disk.deleteByName waitUntil resourceGroup diskName)
                |> Async.Parallel
                |> Async.Ignore
        }

    let private deleteByResource waitUntil (virtualMachine: VirtualMachineResource) =
        async {
            let! cancellationToken = Async.CancellationToken

            do!
                virtualMachine.DeleteAsync(waitUntil, cancellationToken = cancellationToken)
                |> Async.AwaitTask
                |> Async.Ignore
        }

    let deleteByName resourceGroup virtualMachineName =
        let deleteResource = deleteByResource WaitUntil.Completed

        let deleteDisks =
            deleteDisks WaitUntil.Started resourceGroup

        let deleteNetworkInterfaces =
            deleteNetworkInterfaces WaitUntil.Started resourceGroup

        get resourceGroup virtualMachineName
        |> Async.tapAsync deleteResource
        |> Async.tapAsync deleteDisks
        |> Async.tapAsync deleteNetworkInterfaces
        |> Async.Ignore
