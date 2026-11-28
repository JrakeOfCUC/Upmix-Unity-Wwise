# Inspect the compiled first-party Unity runtime assembly. Run once per target:
# platform conditionals can change the graph. Nested and partial types share an owner.
param(
    [Parameter(Mandatory)] [string] $AssemblyPath,
    [Parameter(Mandatory)] [string] $EditorPath
)
$ErrorActionPreference = 'Stop'
$cecil = Join-Path (Split-Path $EditorPath) 'Data/Managed/Unity.Cecil.dll'
Add-Type -LiteralPath $cecil
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path $AssemblyPath).Path)
try {
    $graph = @{}
    $types = $assembly.MainModule.Types | Where-Object {
        $_.Name -notlike '<*' -and $_.Name -notlike 'UnitySourceGeneratedAssemblyMonoScriptTypes*'
    }
    if ($assembly.Name.Name -ne 'Assembly-CSharp' -or !$assembly.MainModule.GetType('SpatialStemPlayer')) {
        throw 'Expected the Upmix first-party Assembly-CSharp.dll'
    }
    foreach ($type in $types) {
        $graph[$type.FullName] = [Collections.Generic.HashSet[string]]::new()
    }

    function Add-TypeEdge($owner, $reference) {
        if ($null -eq $reference) { return }
        if ($reference -is [Mono.Cecil.GenericInstanceType]) {
            foreach ($argument in $reference.GenericArguments) { Add-TypeEdge $owner $argument }
        }
        if ($reference -is [Mono.Cecil.TypeSpecification]) {
            Add-TypeEdge $owner $reference.ElementType
            return
        }
        while ($reference.DeclaringType) { $reference = $reference.DeclaringType }
        $name = $reference.FullName
        if ($name -ne $owner -and $graph.ContainsKey($name)) {
            [void]$graph[$owner].Add($name)
        }
    }

    function Read-Type($owner, $type) {
        Add-TypeEdge $owner $type.BaseType
        foreach ($interface in $type.Interfaces) { Add-TypeEdge $owner $interface.InterfaceType }
        foreach ($field in $type.Fields) { Add-TypeEdge $owner $field.FieldType }
        foreach ($method in $type.Methods) {
            Add-TypeEdge $owner $method.ReturnType
            foreach ($param in $method.Parameters) { Add-TypeEdge $owner $param.ParameterType }
            if (!$method.HasBody) { continue }
            foreach ($local in $method.Body.Variables) { Add-TypeEdge $owner $local.VariableType }
            foreach ($ins in $method.Body.Instructions) {
                $operand = $ins.Operand
                if ($operand -is [Mono.Cecil.TypeReference]) { Add-TypeEdge $owner $operand }
                elseif ($operand -is [Mono.Cecil.MemberReference]) { Add-TypeEdge $owner $operand.DeclaringType }
                if ($operand -is [Mono.Cecil.GenericInstanceMethod]) {
                    foreach ($arg in $operand.GenericArguments) { Add-TypeEdge $owner $arg }
                }
            }
        }
        foreach ($nested in $type.NestedTypes) { Read-Type $owner $nested }
    }
    foreach ($type in $types) { Read-Type $type.FullName $type }
    $visited = @{}
    function Visit($owner, [string[]] $path) {
        if ($owner -in $path) { throw "Runtime dependency cycle: $(($path + $owner) -join ' -> ')" }
        if ($visited[$owner]) { return }
        foreach ($dependency in $graph[$owner]) { Visit $dependency ($path + $owner) }
        $visited[$owner] = $true
    }
    foreach ($owner in ($graph.Keys | Sort-Object)) {
        Visit $owner @()
        "$owner -> $(@($graph[$owner] | Sort-Object) -join ', ')"
    }
}
finally {
    $assembly.Dispose()
}
