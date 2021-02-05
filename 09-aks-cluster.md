# Deploy the Regulated Industries AKS Cluster

Now that the [hub-spoke network is provisioned](./07-cluster-networking.md), the next step in the [AKS Baseline reference implementation for regulated clusters](./) is deploying the AKS cluster and its adjacent Azure resources.

**TODO: Add jump box user setup page.**

## Steps

1. Get the already-deployed, virtual network resource ID that this cluster will be attached to.

   ```bash
   RESOURCEID_VNET_CLUSTERSPOKE=$(az deployment group show -g rg-enterprise-networking-spokes -n spoke-BU0001A0005-01 --query properties.outputs.clusterVnetResourceId.value -o tsv)
   ```

1. Identify your jump box image.

   ```bash
   # If you used a pre-existing image and not the one built by this walk through, replace the command below with the resource id of that image.
   RESOURCEID_IMAGE_JUMPBOX=$(az deployment group show -g rg-bu0001a0005 -n CreateJumpBoxImageTemplate --query 'properties.outputs.distributedImageResourceId.value' -o tsv)
   ```

1. Convert your jump box cloud-init (users) file to Base64.

   ```bash
   CLOUDINIT_BASE64=$(base64 -w 0 jumpBoxCloudInit.yml)
   ```

   If you need to perform this in Powershell, you can achieve the same with this.

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes('jumpBoxCloudInit.yml'))
   ```

1. Deploy the cluster ARM template.

   ```bash
   # [This takes about 20 minutes.]
   az deployment group create -g rg-bu0001a0005 -f cluster-stamp.json -p targetVnetResourceId=${RESOURCEID_VNET_CLUSTERSPOKE} clusterAdminAadGroupObjectId=${AADOBJECTID_GROUP_CLUSTERADMIN} k8sControlPlaneAuthorizationTenantId=${TENANTID_K8SRBAC} appGatewayListenerCertificate=${APP_GATEWAY_LISTENER_CERTIFICATE} aksIngressControllerCertificate=${AKS_INGRESS_CONTROLLER_CERTIFICATE_BASE64} jumpBoxImageResourceId=${RESOURCEID_IMAGE_JUMPBOX} jumpBoxCloudInitAsBase64=${CLOUDINIT_BASE64}
   ```

   > Alteratively, you could set these values in [`azuredeploy.parameters.prod.json`](./azuredeploy.parameters.prod.json) file and deployed as above, using `-p "@azuredeploy.parameters.prod.json"` instead of the individual key-value pairs.

## Container registry note

In this reference implementation, Azure Policy _and_ Azure Firewall are blocking all container registries other than Microsoft Container Registry and your private ACR instance deployed with this reference implementation. This will protect your cluster from unapproved registries being used; which may prevent issues while trying to pull images from a registry which doesn't provide an appropriate SLO and also help meet compliance needs for your container image supply chain.

This deployment creates an SLA-backed Azure Container Registry for your cluster's needs. Your organization may have a central container registry for you to use, or your registry may be tied specifically to your application's infrastructure (as demonstrated in this implementation). **Only use container registries that satisfy the availability and compliance needs of your workload.**

## Operating System (OS) and Kubelet config

The cluster above deploys the default settings for OS and Kubelet configuration that is a recommended starting point for most workloads. If your workload requires kubelet or OS/kernel changes, see [Customize node configuration](https://docs.microsoft.com/azure/aks/custom-node-configuration). Most of these settings are exposed specifically as a mechanism to tune for specific workload performance characteristics (high outbound connections, high filesystem access, large number of concurrent connections, etc.), and not as an OS hardening affordance.

### Next step

:arrow_forward: [Prepare to bootstrap the cluster](./10-registry-quarantine.md)
