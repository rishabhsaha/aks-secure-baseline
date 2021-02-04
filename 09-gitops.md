# Place the Cluster Under GitOps Management

Now that [the AKS cluster](./05-aks-cluster.md) has been deployed, the next step to configure a GitOps management solution on our cluster, Flux in this case.

## Expected results

### Jump box access is validated

While the following process likely would be handled via your deployment pipelines, we are going to use this opportunity to demonstrate cluster management access via Azure Bastion, and show that your cluster cannot be directly accessed locally.

### Flux is configured and deployed

#### Azure Container Registry

Your Azure Container Registry is available to serve more than just your cluster's workload(s). It can also be used to serve any cluster-wide operations tooling you wish installed on your cluster. Your GitOps operator, Flux, is one such tooling. As such, we'll have two container images imported into your private container registry that are required for the functioning of Flux. Likewise, you'll update the related yaml files to point to your specific private container registry. You'll also import other images that are expected to be available in the cluster bootstrapping process.

#### Your Github Repo

Your github repo will be the source of truth for your cluster's configuration. Typically this would be a private repo, but for ease of demonstration, it'll be connected to a public repo (all firewall permissions are set to allow this specific interaction.) You'll be updating a configuration resource for Flux so that it knows to point to your own repo.

## Steps

1. Quarantine Flux and other public baseline security/utility images so you can scan them.

   Quarantining first and third party images is a security best practice. This allows you to get your images onto a container registry and subject them to any sort of security/compliance scrutiny you wish to apply. Once validated, they can then be promoted to being available to your cluster. There are many variations on this pattern, with different tradeoffs for each. For simplicity in this walkthrough we are simply going to upload our images to repository names that starts with `quarantine/`. We'll then show you Azure Security Center's scan of those images, and then you'll import those same images from `quarantine/` to `live/`. We've restricted our cluster to only allow pulling from `live/` and we've built an alert if an image was imported to `live/` from a source other than `quarantine/`. To be clear, this won't block a direct import behavior or validate that the image actually passed quarantine checks. As mentioned, there are other solutions you can use for this pattern that may be more exhaustive.

   ```bash
   # Get your Quarantine Azure Container Registry service name
   ACR_NAME_QUARANTINE=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)
   
   # [Combined this takes about two minutes.]
   az acr import --source ghcr.io/fluxcd/kustomize-controller:v0.6.3 -t quarantine/fluxcd/kustomize-controller:v0.6.3 -n $ACR_NAME_QUARANTINE
   az acr import --source ghcr.io/fluxcd/source-controller:v0.6.3 -t quarantine/fluxcd/source-controller:v0.6.3 -n $ACR_NAME_QUARANTINE
   az acr import --source docker.io/falcosecurity/falco:0.26.2 -t quarantine/falcosecurity/falco:0.26.2 -n $ACR_NAME_QUARANTINE
   az acr import --source docker.io/library/busybox:1.33.0 -t quarantine/library/busybox:1.33.0 -n $ACR_NAME_QUARANTINE
   az acr import --source docker.io/weaveworks/kured:1.6.1 -t quarantine/weaveworks/kured:1.6.1 -n $ACR_NAME_QUARANTINE
   az acr import --source docker.io/envoyproxy/envoy-alpine:v1.15.0 -t quarantine/envoyproxy/envoy-alpine:v1.15.0 -n $ACR_NAME_QUARANTINE

1. Run security audits on images.

   If you had sufficient permissions when we did subscription configuration, Azure Defender for container registries is enabled on your subscription. Azure Defender for container registries will begin scanning all newly imported images in your Azure Container Registry using a Microsoft hosted version of Qualys. The results of those scans will start to be available in Azure Security Center within 15 minutes of import.

   To see the scan results in Azure Security Center, perform the following actions:

   1. Open the [Azure Security Center's **Recommendations** page](https://portal.azure.com/#blade/Microsoft_Azure_Security/SecurityMenuBlade/5).
   1. Under **Controls** expand **Remediate vulnerabilities**.
   1. Click on **Vulnerabilities in Azure Container Registry images should be remediated (powered by Qualys)**.
   1. Expand **Affected resources**.
   1. Click on your ACR instance.

   In here, you can see which container images are unhealthy (had a scan detection), healthy (was scanned, but didn't result in any alerts), and unverified (was unable to be scanned). Unfortunately, Azure Defender for container registries is unable to scan all container types. Also, because your container registry is private, you won't get a list of those "unverified" images here.

   There is no action for you to take, in this walkthrough. This was just a demonstration of Azure Security Center's scanning features. Ultimately, you'll want to build a quarantine pipeline that solves for your needs and aligns with your image deployment strategy.

1. Import Flux and other baseline security/utility images into your container registry.

   > Public container registries are subject to faults such as outages (no SLA) or request throttling. Interruptions like these can be crippling for a system that needs to pull an image _right now_. Also public registries may not support compliance requirements you may have. To minimize the risks of using public registries, store all applicable container images in a registry that you control.

   ```bash
   # Get your Azure Container Registry service name
   ACR_NAME=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.containerRegistryName.value -o tsv)
   
   # [Combined this takes about two minutes.]
   az acr import --source ghcr.io/fluxcd/kustomize-controller:v0.6.3 -n $ACR_NAME
   az acr import --source ghcr.io/fluxcd/source-controller:v0.6.3 -n $ACR_NAME
   az acr import --source docker.io/falcosecurity/falco:0.26.2 -n $ACR_NAME
   az acr import --source docker.io/library/busybox:1.33.0 -n $ACR_NAME
   az acr import --source docker.io/weaveworks/kured:1.6.1 -n $ACR_NAME
   az acr import --source docker.io/envoyproxy/envoy-alpine:v1.15.0 -n $ACR_NAME
   ```

1. Update kustomization files to use images from your container registry.

   ```bash
   cd cluster-manifests
   grep -lr REPLACE_ME_WITH_YOUR_ACRNAME --include=kustomization.yaml | xargs sed -i "s/REPLACE_ME_WITH_YOUR_ACRNAME/${ACR_NAME}/g"

   git commit -a -m "Update bootstrap deployments to use images from my ACR instead of public container registries."
   ```

1. Update flux to pull from your repo instead of the mspnp repo.

   ```bash
   sed -i "s/REPLACE_ME_WITH_YOUR_GITHUBACCOUNTNAME/${GITHUB_ACCOUNT_NAME}/" flux-system/gotk-sync.yaml

   git commit -a -m "Update Flux to pull from my fork instead of the upstream Microsoft repo."
   ```

1. Push those two changes to your repo.

   ```bash
   git push
   ```

1. Connect to a jump box node via Azure Bastion.

   If this is the first time you've used Azure Bastion, here is a detailed walk through of this process.

   1. Open the [Azure Portal](https://portal.azure.com).
   2. Navigate to the **rg-bu0001a0005** resource group.
   3. Click on the Virtual Machine Scale Set resource named **vmss-jumpboxes**.
   4. Click **Instances**.
   5. Click the name of any of the two listed instances. E.g. **vmss-jumpboxes_0**
   6. Click **Connect** -> **Bastion** -> **Use Bastion**.
   7. Fill in the username field with one of the users from your customized `jumpBoxCloudInit.yml` file. E.g. **opsuser01**
   8. Select **SSH Private Key from Local File** and select your private key file for that specific user.
   9. Provide your SSH passphrase in **SSH Passphrase** if your private key is protected with one.
   10. Click **Connect**.
   11. For enhanced "copy-on-select" & "paste-on-right-click" support, your browser may request your permission to support those features. It's recommended that you _Allow_ that feature. If you don't, you'll have to use the **>>** flyout on the screen to perform copy & paste actions.
   12. Welcome to your jump box!

1. From your Azure Bastion connection, log into your Azure RBAC tenant and select your subscription.

   The following command will perform a device login. Ensure you're logging in with the Azure AD user that has access to your AKS resources (i.e. the one you did your deployment with.)

   ```bash
   az login
   # This will give you a link to https://microsoft.com/devicelogin where you can enter 
   # the provided code and perform authentication.

   # Ensure you're on the correct subscription
   az account show

   # If not, select the correct subscription
   az account set -s <subscription name or id>
   ```

1. _From your Azure Bastion connection_, get your AKS credentials and set your `kubectl` context to your cluster.

   ```bash
   AKS_CLUSTER_NAME=$(az deployment group show -g rg-bu0001a0005 -n cluster-stamp --query properties.outputs.aksClusterName.value -o tsv)

   az aks get-credentials -g rg-bu0001a0005 -n $AKS_CLUSTER_NAME
   ```

1. _From your Azure Bastion connection_, test cluster access and authenticate as a cluster admin user.

   The following command will force you to authenticate into your AKS cluster's control plane. This will start yet another device login flow. For this one (**Azure Kubernetes Service AAD Client**), log in with a user that is a member of your cluster admin group in the Azure AD tenet you selected to be used for Kubernetes Cluster API RBAC. This is the user you're performing cluster management commands (e.g. `kubectl`) as.

   ```bash
   kubectl get nodes
   ```

   If all is successful you should see something like:

   ```output
   NAME                                  STATUS   ROLES   AGE   VERSION
   aks-npinscope01-26621167-vmss000000   Ready    agent   20m   v1.19.6
   aks-npinscope01-26621167-vmss000001   Ready    agent   20m   v1.19.6
   aks-npooscope01-26621167-vmss000000   Ready    agent   20m   v1.19.6
   aks-npooscope01-26621167-vmss000001   Ready    agent   20m   v1.19.6
   aks-npsystem-26621167-vmss000000      Ready    agent   20m   v1.19.6
   aks-npsystem-26621167-vmss000001      Ready    agent   20m   v1.19.6
   aks-npsystem-26621167-vmss000002      Ready    agent   20m   v1.19.6
   ```

1. _From your Azure Bastion connection_, bootstrap Flux.

   ```bash
   git clone https://github.com/[[YOUR_GITHUB_ACCOUNT_NAME]]/aks-regulated-cluster
   cd aks-regulated-cluster

   # Apply the Flux CRDs before applying the rest of flux (to avoid a race condition in the sync settings)
   kubectl apply -f cluster-manifests/flux-system/gotk-crds.yaml
   kubectl apply -k cluster-manifests/flux-system
   ```

   Validate that flux has been bootstrapped.

   ```bash
   kubectl wait --namespace flux-system --for=condition=available deployment/source-controller --timeout=90s

   # If you have flux cli installed you can also inspect using the following commands
   # (The default jump box image created with this walkthrough has the flux cli installed.)
   flux check --components source-controller,kustomize-controller
   flux get sources git
   flux get kustomizations
   ```

1. Disconnect from the jump box and Azure Bastion.

Generally speaking, this will be the last time you should need to use direct cluster access tools like `kubectl` for day-to-day configuration operations on this cluster (outside of live-site situations). Between ARM for Azure Resource definitions and the application of manifests via Flux, all normal configuration activities can be performed without the need to use `kubectl`. You will however see us use it for the upcoming workload deployment. This is because the SDLC component of workloads are not in scope for this reference implementation, as this is focused the infrastructure and baseline configuration.

Typically of the above bootstrapping steps would be codified in a release pipeline so that there would be NO NEED to perform any steps manually. We're performing the steps manually here, like we have with all content so far for illustrative purposes of the steps required. Once you have a safe deployment practice documented (both for internal team reference and for compliance needs), you can then put those actions into an auditable deployment pipeline, that combines deploying the infrastructure with the immediate follow up bootstrapping the cluster. Your workload(s) have a distinct lifecycle from your cluster and as such are managed via another pipeline. But bootstrapping your cluster should be seen as a direct and immediate continuation of the deployment of your cluster.

## Flux configuration

The Flux implementation in this reference architecture is intentionally simplistic. Flux is configured to simply monitoring manifests in ALL namespaces. It doesn't account for concepts like:

* Built-in [bootstrapping support](https://toolkit.fluxcd.io/guides/installation/#bootstrap).
* [Multi-tenancy](https://github.com/fluxcd/flux2-multi-tenancy)
* [Private GitHub repos](https://toolkit.fluxcd.io/components/source/gitrepositories/#ssh-authentication)
* Kustomization [under/overlays](https://kubernetes.io/docs/tasks/manage-kubernetes-objects/kustomization/#bases-and-overlays)
* Flux's [Notifications controller](https://github.com/fluxcd/notification-controller) to [alert on changes](https://toolkit.fluxcd.io/guides/notifications/).
* Flux's [Helm controller](https://github.com/fluxcd/helm-controller) to [manage helm releases](https://toolkit.fluxcd.io/guides/helmreleases/)
* Flux's [monitoring](https://toolkit.fluxcd.io/guides/monitoring/) features.

This reference implementation isn't going to dive into the nuances of git manifest organization. A lot of that depends on your namespacing, multi-tenant needs, multi-stage (dev, pre-prod, prod) deployment needs, multi-cluster needs, etc. The key takeaway here is to ensure that you're managing your Kubernetes resources in a declarative manner with a reconcile loop, to achieve desired state configuration within your cluster. Ensuring your cluster internally is managed by a single, appropriately-privileged, observable pipeline will aide in compliance. You'll have a git trail that aligns with a log trail from your GitOps toolkit.

## Public dependencies

As with any dependency your cluster or workload has, you'll want to minimize or eliminate your reliance on services in which you do not have an SLO or do not meet your observability/compliance requirements. Your cluster's GitOps operator(s) should rely on a git repository that satisfies your reliability & compliance requirements. Consider using a git-mirror approach to bring your cluster dependencies to be "network local" and provide a fault-tolerant syncing mechanism from centralized working repositories (like your organization's GitHub Enterprise private repositories). Following an approach like this will air gap git repositories as an external dependency, at the cost of added complexity.

## Security tooling

While Azure Kubernetes Service, Azure Defender, and Azure Policy offers a secure platform foundation; the inner workings of your cluster are more of a relationship with you and Kubernetes than you and Azure. To that end, most customers bring their own security solutions that solve for their specific compliance and organizational requirements within their clusters. They often bring in ISV solutions like [Aqua Security](https://www.aquasec.com/solutions/azure-container-security/), [Prisma Cloud Compute](hhttps://docs.paloaltonetworks.com/prisma/prisma-cloud/prisma-cloud-admin-compute/install/install_kubernetes.html), [StackRox](https://www.stackrox.com/solutions/microsoft-azure-security/), or [Sysdig](https://sysdig.com/partners/microsoft-azure/) to name a few. These solutions offer a suite of added security and reporting controls to your platform, but also come with their own licensing and support agreements.

Common features offered in ISV solutions like these:

* File Integrity Monitoring (FIM)
* Anti-Virus solutions
* CVE Detection against admission requests and executing images
* Advanced network segmentation
* Dangerous runtime container activity
* Workload level CIS benchmark reporting

Your dependency on or choice of in-cluster tooling to achieve your compliance needs cannot be suggested as a "one-size fits all" in this reference implementation. However, as a reminder of the need to solve for these, the Flux bootstrapping above deployed a dummy FIM and AV solution. **They are not functioning as a real FIM or AV**, simply a visual reminder that you will need to bring a suitable solution.

This reference implementation also installs a simplistic deployment of [Falco](https://falco.org/). It is not configured for alerts, nor tuned to any specific needs. It uses the default rules as they were defined when its manifests were generated. This is also being installed for illustrative purposes, and you're encouraged to evaluate if a solution like Falco is relevant to you. If so, in your final implementation, review and tune its deployment to fit your needs (E.g. add custom rules like [CVE detection](https://artifacthub.io/packages/search?ts_query_web=cve&org=falco), [sudo usage](https://artifacthub.io/packages/falco/security-hub/admin-activities), [basic FIM](https://artifacthub.io/packages/falco/security-hub/file-integrity-monitoring), [SSH Connection monitoring](https://artifacthub.io/packages/falco/security-hub/ssh-connections), and [Traefik containment](https://artifacthub.io/packages/falco/security-hub/traefik)). This tooling, as most security tooling will be, is highly-privileged within your cluster. Usually running a DaemonSets with access to the underlying node in a manor that is well beyond any typical workload in your cluster.

You should ensure all necessary tooling and related reporting/alerting is applied as part of your initial bootstrapping process to ensure coverage _immediately_ after cluster creation.

## Container registry quarantine pattern

While Azure Defender for container registries is a natural fit for scanning images in Azure Container Registry, it unfortunately cannot be used in conjunction with an ACR that is network restricted with Private Link, such as the one in this walkthrough. This ACR instance is exclusively exposed for your cluster, and no other access.

It's a common desire to want to implement a container registry quarantine pattern; where you first push to a staging container registry that is exposed to your tooling of choice, such as Qualys (stand alone or as part of Azure Defender for container registries). The images undergo any scanning desired, and once past that gate is then imported into the final production registry. Azure Container Registry currently does not have this pattern implemented for General Availability in a single-ACR instance topology; so the pattern is often implemented using a series of registries as part of your deployment process.

The quarantine pattern is ideal for detecting issues with new images, but continuous scanning is also desirable as CVEs can be found at any time for your images that are in use. Azure Defender for container registries can perform continuous scanning, but only for recently pull images. This walkthrough does not implement this pattern, but it is strongly recommended that you find a suitable workflow that allows your images (your own first party and third party) to pass through a security scanning gate, and also an ongoing periodic scanning process. You could perform this in the cluster with a security agent (usually via an Admission Controller shipped with the agent) or external to the cluster in your container registry, or both.

### Next step

:arrow_forward: [Prepare for the workload by installing its prerequisites](./07-workload-prerequisites.md)
