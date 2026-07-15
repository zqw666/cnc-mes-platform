import os
import glob

backend = r'D:\web-ui\backend'
subs = ['change_gripper','load_machine','machine_status','prepare_material',
        'rack_operate','scan','start_process','transfer','unload_machine','unload_material']

# DLL paths relative to each sub-project directory
# Each sub-project is at D:\web-ui\backend\<name>\<name>.csproj
# DLLs are at:
#   ..\CommonOperateDll\DataCommonOperate\bin\Debug\DataCommonOperate.dll
#   ..\CommonOperateDll\DataCommonOperate\bin\Debug\mysql.data.dll
#   ..\FTP\FTP\bin\Debug\FTP.dll
#   ..\FileOperate\FileOperate\bin\Debug\FileOperate.dll
#   ..\PLCOP\PLCOP\bin\Debug\PLCOP.dll
#   ..\PLCOP\PLCOP\bin\Debug\mcOMRON.dll
#   ..\SerialPort\SerialPort\bin\Debug\SerialPort.dll

dll_refs = [
    ('DataCommonOperate', r'..\CommonOperateDll\DataCommonOperate\bin\Debug\DataCommonOperate.dll'),
    ('MySql.Data', r'..\CommonOperateDll\DataCommonOperate\bin\Debug\mysql.data.dll'),
    ('FTP', r'..\FTP\FTP\bin\Debug\FTP.dll'),
    ('FileOperate', r'..\FileOperate\FileOperate\bin\Debug\FileOperate.dll'),
    ('PLCOP', r'..\PLCOP\PLCOP\bin\Debug\PLCOP.dll'),
    ('mcOMRON', r'..\PLCOP\PLCOP\bin\Debug\mcOMRON.dll'),
    ('SerialPort', r'..\SerialPort\SerialPort\bin\Debug\SerialPort.dll'),
]

for sub in subs:
    csproj_path = os.path.join(backend, sub, f'{sub}.csproj')
    if not os.path.exists(csproj_path):
        print(f'SKIP (not found): {csproj_path}')
        continue
    
    with open(csproj_path, 'r', encoding='utf-8-sig') as f:
        content = f.read()
    
    # Check if it's SDK-style (has <Project Sdk=)
    is_sdk = '<Project Sdk=' in content
    
    # Find existing references to avoid duplicates
    existing_refs = set()
    import re
    for m in re.finditer(r'<Reference Include="([^"]+)"', content):
        # Extract just the assembly name (before , if any)
        ref_name = m.group(1).split(',')[0].strip()
        existing_refs.add(ref_name)
    
    print(f'\n=== {sub} (SDK={is_sdk}) ===')
    print(f'  Existing refs: {existing_refs}')
    
    # Build new reference entries
    new_refs = []
    for ref_name, hint_path in dll_refs:
        if ref_name in existing_refs:
            print(f'  SKIP (exists): {ref_name}')
            # Check if HintPath needs updating
            continue
        new_refs.append(f'    <Reference Include="{ref_name}">\n      <HintPath>{hint_path}</HintPath>\n    </Reference>')
        print(f'  ADD: {ref_name}')
    
    if not new_refs:
        print(f'  No new refs to add')
        continue
    
    # Insert references before </ItemGroup> that contains references, or before </Project>
    refs_xml = '\n'.join(new_refs)
    
    # Find the right place to insert - look for an ItemGroup with Reference items
    # or create one
    if '<ItemGroup>' in content and '<Reference' in content:
        # Insert after the last </Reference> in the Reference ItemGroup
        # Find the ItemGroup that contains Reference elements
        last_ref_end = content.rfind('</Reference>')
        if last_ref_end != -1:
            insert_pos = last_ref_end + len('</Reference>')
            content = content[:insert_pos] + '\n' + refs_xml + content[insert_pos:]
    elif '<ItemGroup>' in content:
        # Insert in existing ItemGroup
        first_ig_end = content.find('</ItemGroup>')
        if first_ig_end != -1:
            content = content[:first_ig_end] + refs_xml + '\n  ' + content[first_ig_end:]
    else:
        # No ItemGroup at all, add one before </Project>
        project_end = content.rfind('</Project>')
        if project_end != -1:
            content = content[:project_end] + '  <ItemGroup>\n' + refs_xml + '\n  </ItemGroup>\n' + content[project_end:]
    
    with open(csproj_path, 'w', encoding='utf-8-sig') as f:
        f.write(content)
    
    print(f'  UPDATED: {csproj_path}')

print('\nDone!')
