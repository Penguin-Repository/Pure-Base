<#
 Copyright 2026 Penguin

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

     http://www.apache.org/licenses/LICENSE-2.0

 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.
#>

# Provides shared fixed validation-scene property and oracle helpers.
function Add-Failure {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter(Mandatory = $true)][string]$Message
    )

    [void]$Failures.Add([ordered]@{ code = $Code; message = $Message })
}

function Get-IntegerProperty {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact is missing integer property '$Name'."
        return $null
    }
    $typeCode = [System.Type]::GetTypeCode($property.Value.GetType())
    if ($typeCode -notin @([System.TypeCode]::SByte, [System.TypeCode]::Byte, [System.TypeCode]::Int16, [System.TypeCode]::UInt16, [System.TypeCode]::Int32, [System.TypeCode]::UInt32, [System.TypeCode]::Int64, [System.TypeCode]::UInt64)) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact property '$Name' is not an integer."
        return $null
    }
    return [int]$property.Value
}

function Get-ArrayPropertyItems {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value -or $property.Value -is [string] -or -not ($property.Value -is [System.Collections.IEnumerable])) {
        Add-Failure -Failures $Failures -Code $Code -Message "Artifact is missing array property '$Name'."
        return $null
    }
    return @($property.Value)
}

function Get-ValidationSceneOracleValue {
    param(
        [Parameter(Mandatory = $true)]$Scene,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[object]]$Failures,
        [Parameter(Mandatory = $true)][string]$Code
    )

    switch ($Name) {
        'staticLightmapCount' { return Get-IntegerProperty -Object $Scene -Name 'staticLightmapCount' -Failures $Failures -Code $Code }
        'staticRendererAssignmentCount' {
            $items = Get-ArrayPropertyItems -Object $Scene -Name 'staticLightmaps' -Failures $Failures -Code $Code
            if ($null -eq $items) { return $null }
            return $items.Count
        }
        'metaReadbackCount' {
            $items = Get-ArrayPropertyItems -Object $Scene -Name 'metaAlbedo' -Failures $Failures -Code $Code
            if ($null -eq $items) { return $null }
            return $items.Count
        }
        'shadowDeltaPixelCount' { return Get-IntegerProperty -Object $Scene -Name 'shadowChangedPixelCount' -Failures $Failures -Code $Code }
        'warmedRepresentativeVariantCount' {
            $items = Get-ArrayPropertyItems -Object $Scene -Name 'variants' -Failures $Failures -Code $Code
            if ($null -eq $items) { return $null }
            foreach ($item in $items) {
                if ($null -eq $item -or $null -eq $item.PSObject.Properties['warmed'] -or $item.warmed -ne $true -or $null -eq $item.PSObject.Properties['variantCount']) {
                    Add-Failure -Failures $Failures -Code $Code -Message 'Variant evidence is missing a warmed representative variant.'
                    return $null
                }
                try {
                    if ([int]$item.variantCount -ne 1) {
                        Add-Failure -Failures $Failures -Code $Code -Message 'Variant evidence does not contain exactly one warmed representative variant per entry.'
                        return $null
                    }
                }
                catch {
                    Add-Failure -Failures $Failures -Code $Code -Message 'Variant evidence has a non-integer variantCount.'
                    return $null
                }
            }
            return $items.Count
        }
        default { throw "Unsupported fixed validation-scene oracle '$Name'." }
    }
}
