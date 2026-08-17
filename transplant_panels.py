import re

def transplant(source_file, target_file, brush_to_replace, target_brush):
    with open(source_file, 'r') as f:
        src = f.read()
    
    # Extract Part_PanelsContainer
    match = re.search(r'(<Grid Grid\.Column="1" IsVisible="\{Binding !IsPanelHostedExternally\}" x:Name="Part_PanelsContainer">.*?</Grid> <!-- End Part_PanelsContainer -->)', src, re.DOTALL)
    if not match:
        print("Could not find Part_PanelsContainer in source")
        return
        
    panels_xml = match.group(1)
    # Replace brush
    panels_xml = panels_xml.replace(brush_to_replace, target_brush)
    
    with open(target_file, 'r') as f:
        tgt = f.read()
        
    tgt = re.sub(r'<Grid Grid\.Column="1" IsVisible="\{Binding !IsPanelHostedExternally\}" x:Name="Part_PanelsContainer">.*?</Grid> <!-- End Part_PanelsContainer -->', panels_xml, tgt, flags=re.DOTALL)
    
    with open(target_file, 'w') as f:
        f.write(tgt)

if __name__ == '__main__':
    transplant('/home/abeng/BMachine.v2/src/BMachine.UI/Views/EditingCardListView.axaml',
               '/home/abeng/BMachine.v2/src/BMachine.UI/Views/LateCardListView.axaml',
               'AccentEditingBrush', 'AccentLateBrush')
