# Configure AKS Ingress Controller with Azure Key Vault integration

Previously you have configured [workload prerequisites](./12-workload-prerequisites.md). These steps configure Traefik, the AKS ingress solution used in this reference implementation, so that it can securely expose the web app to your Application Gateway.


## Expected Outcome

* A Pod Managed Identity (`podmi-ingress-controller`) is deployed to the `a0005` namespace and ready to be bound via the name `podmi-ingress-controller`.
* TODO

## Setting up Pod Managed Identity

Pod Managed Identity goes beyond being just a workload concern. A Pod Managed Identity is a User Managed Identity resource in Azure, and needs to be managed like any other resource in Azure. Likewise, the cluster's control plane Managed Identity must have the authority to assign this identity to the nodepools (VMSS resources). As such, before a workload can use a managed identity the cluster operator needs to make it "available" to the workload. In this walkthrough, we are going to deploy a slight variant of the prior cluster stamp ARM template that will associated a User Managed Identity with the cluster, which will then make it available for your workload. Because Pod Managed Identity is installed as a cluster add-on (vs through the manual open source installation option), it is critical that all pod identities are managed via the cluster's ARM template. **Mixing imperative management of identities (`az aks pod-identity ...` or `kubectl`) and declarative management of identities (ARM/Terraform template) will lead to an unexpected removal of identities, which will cause an outage in your workload.**

### Steps

1. Deploy the cluster ARM template that has been updated with the Pod Managed Identity assignment.

   This is the same cluster stamp ARM template you deployed before, but with a couple updates to include the Pod Managed Identity that needs to be made available to the workload. You'll be using the exact same parameters as before. If you wish to see the difference, you can diff them HERE (TODO).

   ```bash
   # [This takes about 10 minutes.]
   az deployment group create -g rg-bu0001a0005 -f cluster-stamp.v1.json -p targetVnetResourceId=${RESOURCEID_VNET_CLUSTERSPOKE} clusterAdminAadGroupObjectId=${AADOBJECTID_GROUP_CLUSTERADMIN} k8sControlPlaneAuthorizationTenantId=${TENANTID_K8SRBAC} appGatewayListenerCertificate=${APP_GATEWAY_LISTENER_CERTIFICATE} aksIngressControllerCertificate=${AKS_INGRESS_CONTROLLER_CERTIFICATE_BASE64} jumpBoxImageResourceId=${RESOURCEID_IMAGE_JUMPBOX} jumpBoxCloudInitAsBase64=${CLOUDINIT_BASE64}

   # Or if you used the parameters file...
   #az deployment group create -g rg-bu0001a0005 -f cluster-stamp.v1.json -p "@azuredeploy.parameters.prod.json"
   ```

1. _From your Azure Bastion connection_, confirm your Pod Managed Identity now exists.

   ```bash
   kubectl describe AzureIdentity,AzureIdentityBinding -n a0005
   ```

   This will show you the Azure identity resources that were created via the ARM template changes applied in the prior step. This means that any workload in the `a0005` namespace that wishes to identify itself as the Azure resource `podmi-ingress-controller` can do so by adding a `aadpodidbinding: podmi-ingress-controller` label to their pod deployment. In this walkthrough, our ingress controller, Traefik, will be using that identity to pull its TLS certificate from Azure Key Vault.

## Steps

1. Ensure Flux has created the following namespace

   ```bash
   # press Ctrl-C once you receive a successful response
   kubectl get ns a0005 -w
   ```


1. Create the Traefik's Secret Provider Class resource

   > The Ingress Controller will be exposing the wildcard TLS certificate you created in a prior step. It uses the Azure Key Vault CSI Provider to mount the certificate which is managed and stored in Azure Key Vault. Once mounted, Traefik can use it.
   >
   > Create a `SecretProviderClass` resource with with your Azure Key Vault parameters for the [Azure Key Vault Provider for Secrets Store CSI driver](https://github.com/Azure/secrets-store-csi-driver-provider-azure).

   ```bash
   cat <<EOF | kubectl apply -f -
   apiVersion: secrets-store.csi.x-k8s.io/v1alpha1
   kind: SecretProviderClass
   metadata:
     name: aks-ingress-contoso-com-tls-secret-csi-akv
     namespace: a0008
   spec:
     provider: azure
     parameters:
       usePodIdentity: "true"
       keyvaultName: "${KEYVAULT_NAME}"
       objects:  |
         array:
           - |
             objectName: traefik-ingress-internal-aks-ingress-contoso-com-tls
             objectAlias: tls.crt
             objectType: cert
           - |
             objectName: traefik-ingress-internal-aks-ingress-contoso-com-tls
             objectAlias: tls.key
             objectType: secret
       tenantId: "${TENANT_ID}"
   EOF
   ```

1. Import the Traefik container image to your container registry

   > Public container registries are subject to faults such as outages (no SLA) or request throttling. Interruptions like these can be crippling for an application that needs to pull an image _right now_. To minimize the risks of using public registries, store all applicable container images in a registry that you control, such as the SLA-backed Azure Container Registry.

   ```bash
   # Get your ACR cluster name
   export ACR_NAME=$(az deployment group show --resource-group rg-bu0001a0008 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)

   # Import ingress controller image hosted in public container registries
   az acr import --source docker.io/library/traefik:2.2.1 -n $ACR_NAME
   ```

1. Install the Traefik Ingress Controller

   > Install the Traefik Ingress Controller; it will use the mounted TLS certificate provided by the CSI driver, which is the in-cluster secret management solution.

   > If you used your own fork of this GitHub repo, update the one `image:` value in [`traefik.yaml`](./workload/traefik.yaml) to reference your container registry instead of the default public container registry and change the URL below to point to yours as well.

   :warning: Deploying the traefik `traefik.yaml` file unmodified from this repo will be deploying your workload to take dependencies on a public container registry. This is generally okay for learning/testing, but not suitable for production. Before going to production, ensure _all_ image references are from _your_ container registry or another that you feel confident relying on.

   ```bash
   kubectl apply -f https://raw.githubusercontent.com/mspnp/aks-secure-baseline/main/workload/traefik.yaml
   ```

1. Wait for Traefik to be ready

   > During Traefik's pod creation process, AAD Pod Identity will need to retrieve token for Azure Key Vault. This process can take time to complete and it's possible for the pod volume mount to fail during this time but the volume mount will eventually succeed. For more information, please refer to the [Pod Identity documentation](https://github.com/Azure/secrets-store-csi-driver-provider-azure/blob/master/docs/pod-identity-mode.md).

   ```bash
   kubectl wait --namespace a0008 --for=condition=ready pod --selector=app.kubernetes.io/name=traefik-ingress-ilb --timeout=90s
   ```

### Next step

:arrow_forward: [Deploy the Workload](./14-workload.md)
