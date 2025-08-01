using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Linq;
using Dynamo.Core;
using Dynamo.Selection;
using Dynamo.Utilities;
using Dynamo.Engine;
using Dynamo.Engine.NodeToCode;
using Dynamo.Graph;
using Dynamo.Graph.Annotations;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Nodes.ZeroTouch;
using Dynamo.Graph.Notes;
using Dynamo.Graph.Workspaces;

namespace Dynamo.Models
{
    internal delegate void RecordableCommandHandler(DynamoModel.RecordableCommand command);

    partial class DynamoModel
    {
        internal event RecordableCommandHandler CommandStarting;
        internal event RecordableCommandHandler CommandCompleted;

        static internal event Action RequestHideNodeAutoCompleteBar;

        /// <summary>
        /// Executes specified command
        /// </summary>
        /// <param name="command">Command to execute</param>
        public void ExecuteCommand(RecordableCommand command)
        {
            if (CommandStarting != null)
                CommandStarting(command);

            command.Execute(this);

            if (CommandCompleted != null)
                CommandCompleted(command);
        }

        private PortModel[] activeStartPorts;
        private PortModel firstStartPort;

        protected virtual void OpenFileImpl(OpenFileCommand command)
        {
            string filePath = command.FilePath;
            bool forceManualMode = command.ForceManualExecutionMode;
            bool isTemplate = command.IsTemplate;
            OpenFileFromPath(filePath, forceManualMode);

            //clear the clipboard to avoid copying between dyns
            //ClipBoard.Clear();
        }

        protected virtual void OpenTemplateImpl(OpenFileCommand command)
        {
            string filePath = command.FilePath;
            bool forceManualMode = command.ForceManualExecutionMode;
            OpenTemplateFromPath(filePath, forceManualMode);
        }

        protected virtual void OpenFileFromJsonImpl(OpenFileFromJsonCommand command)
        {
            string fileContents = command.FileContents;
            bool forceManualMode = command.ForceManualExecutionMode;
            OpenFileFromJson(fileContents, forceManualMode);
        }

        protected virtual void InsertFileImpl(InsertFileCommand command)
        {
            string filePath = command.FilePath;
            bool forceManualMode = command.ForceManualExecutionMode;
            InsertFileFromPath(filePath, forceManualMode);
        }

        private void RunCancelImpl(RunCancelCommand command)
        {
            var model = CurrentWorkspace as HomeWorkspaceModel;
            if (model != null)
                model.Run();
        }

        private void ForceRunCancelImpl(ForceRunCancelCommand command)
        {
            ForceRun();
        }

        private void CreateNodeImpl(CreateNodeCommand command)
        {
            RequestHideNodeAutoCompleteBar?.Invoke();

            var node = GetNodeFromCommand(command);
            if (node == null)
            {
                throw new Exception("Could not create node: " + command.Name);
            }

            node.X = command.X;
            node.Y = command.Y;
            node.IsTransient = command.IsTransient;

            AddNodeToCurrentWorkspace(node, centered: command.DefaultPosition);
            CurrentWorkspace.RecordCreatedModel(node);
        }

        private void CreateAndConnectNodeImpl(CreateAndConnectNodeCommand command)
        {
            using (CurrentWorkspace.UndoRecorder.BeginActionGroup())
            {
                CreateAndConnectNodeImplWithUndoGroup(command);
            }
        }

        /// <summary>
        /// This method assumes that there exists an undo-redo action group already
        /// that can be used to record creation and deletion of models.
        /// </summary>
        /// <param name="command"></param>
        private void CreateAndConnectNodeImplWithUndoGroup(CreateAndConnectNodeCommand command)
        {
            var newNode = CreateNodeFromNameOrType(command.ModelGuid, command.NewNodeName);

            if (newNode == null) return;

            newNode.X = command.X;
            newNode.Y = command.Y;

            var existingNode = CurrentWorkspace.GetModelInternal(command.ModelGuids.ElementAt(1)) as NodeModel;

            if (existingNode == null) return;

            AddNodeToCurrentWorkspace(newNode, false, command.AddNewNodeToSelection);
            CurrentWorkspace.UndoRecorder.RecordCreationForUndo(newNode);

            PortModel inPortModel, outPortModel;
            if (command.CreateAsDownstreamNode)
            {
                // Connect output port of Existing Node to input port of New node
                outPortModel = existingNode.OutPorts[command.OutputPortIndex];
                inPortModel = newNode.InPorts[command.InputPortIndex];
            }
            else
            {
                // Connect output port of New Node to input port of existing node
                outPortModel = newNode.OutPorts[command.OutputPortIndex];
                inPortModel = existingNode.InPorts[command.InputPortIndex];
            }

            var models = GetConnectorsToAddAndDelete(inPortModel, outPortModel);

            foreach (var modelPair in models)
            {
                switch (modelPair.Value)
                {
                    case UndoRedoRecorder.UserAction.Creation:
                        CurrentWorkspace.UndoRecorder.RecordCreationForUndo(modelPair.Key);
                        break;
                    case UndoRedoRecorder.UserAction.Deletion:
                        CurrentWorkspace.UndoRecorder.RecordDeletionForUndo(modelPair.Key);
                        break;
                }
            }
        }

        private NodeModel GetNodeFromCommand(CreateNodeCommand command)
        {
            if (command.Node != null)
            {
                return command.Node;
            }

            if (command.NodeXml != null)
            {
                // command was deserialized, we must create the node directly
                return NodeFactory.CreateNodeFromXml(command.NodeXml, SaveContext.Save, currentWorkspace.ElementResolver);
            }

            // legacy command, hold on to your butts
            var name = command.Name;
            var nodeId = command.ModelGuid;

            // find nodes with of the same type with the same GUID
            var query = CurrentWorkspace.Nodes.Where(n => n.GUID.Equals(nodeId) && n.Name.Equals(name));

            // safely ignore a node of the same type with the same GUID
            if (query.Any())
            {
                return query.First();
            }

            // To be used in the event it's a custom node we're making.
            Guid customNodeId;

            if (command is CreateProxyNodeCommand)
            {
                var proxyCommand = command as CreateProxyNodeCommand;

                return NodeFactory.CreateProxyNodeInstance(nodeId, name,
                    proxyCommand.NickName, proxyCommand.Inputs, proxyCommand.Outputs);
            }

            // Then, we have to figure out what kind of node to make, based on the name.

            NodeModel node = CreateNodeFromNameOrType(nodeId, name);
            if (node != null) return node;

            // And if that didn't work, then it must be a custom node.
            if (Guid.TryParse(name, out customNodeId))
            {
                node = CustomNodeManager.CreateCustomNodeInstance(customNodeId, null, false);
                node.GUID = nodeId;
                return node;
            }

            // We're out of ideas, log an error.
            Logger.LogError("Could not create instance of node with name: " + name);
            return null;
        }

        internal NodeModel CreateNodeFromNameOrType(Guid nodeId, string name, bool isTransient = false)
        {
            NodeModel node;

            // First, we check for a DSFunction by looking for a FunctionDescriptor
            var functionItem = LibraryServices.GetFunctionDescriptor(name);
            if (functionItem != null)
            {
                node = (functionItem.IsVarArg)
                    ? new DSVarArgFunction(functionItem) as NodeModel
                    : new DSFunction(functionItem);
                node.GUID = nodeId;
                node.IsTransient = isTransient;
                return node;
            }

            // If that didn't work, let's try using the NodeFactory
            if (NodeFactory.CreateNodeFromTypeName(name, out node))
            {
                node.GUID = nodeId;
                node.IsTransient = isTransient;
            }
            return node;
        }

        private void CreateNoteImpl(CreateNoteCommand command)
        {
            NoteModel noteModel = CurrentWorkspace.AddNote(
                command.DefaultPosition,
                command.X,
                command.Y,
                command.NoteText,
                command.ModelGuid);

            CurrentWorkspace.RecordCreatedModel(noteModel);
        }

        private void CreateAnnotationImpl(CreateAnnotationCommand command)
        {
            AnnotationModel annotationModel = currentWorkspace.AddAnnotation(
                command.AnnotationText, 
                command.AnnotationDescriptionText, 
                command.ModelGuid);
    
            CurrentWorkspace.RecordCreatedModel(annotationModel);
        }

        private void SelectModelImpl(SelectModelCommand command)
        {
            // Empty ModelGuid means clear selection.
            if (command.ModelGuid == Guid.Empty)
            {
                ClearSelectionAndRecordUndo();                
                return;
            }

            foreach (var guid in command.ModelGuids)
            {
                var model = CurrentWorkspace.GetModelInternal(guid);

                if (model == null) return;

                if (!model.IsSelected)
                {
                    if (!command.Modifiers.HasFlag(ModifierKeys.Shift) && command.ModelGuids.Count() == 1)
                        ClearSelectionAndRecordUndo();

                    AddSelectionAndRecordUndo(model);
                }
                else
                {
                    if (command.Modifiers.HasFlag(ModifierKeys.Shift))
                        DynamoSelection.Instance.Selection.Remove(model);
                }

            }
        }

        private void AddSelectionAndRecordUndo(ModelBase model)
        {
            try
            {
                WorkspaceModel.RecordModelsForModification(new List<ModelBase>() { model }, CurrentWorkspace.UndoRecorder);
                DynamoSelection.Instance.Selection.AddUnique(model);
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to add model(s) to selection." + "\n" + ex.Message);
            }
        }

        private void ClearSelectionAndRecordUndo()
        {
            List<ModelBase> models = new List<ModelBase>();

            foreach (var selection in DynamoSelection.Instance.Selection)
            {
                var modelBase = (ModelBase)selection;
                models.Add(modelBase);
            }

            WorkspaceModel.RecordModelsForModification(models, CurrentWorkspace.UndoRecorder);

            DynamoSelection.Instance.ClearSelection();
        }

        private void MakeConnectionImpl(MakeConnectionCommand command)
        {
            RequestHideNodeAutoCompleteBar?.Invoke();

            Guid nodeId = command.ModelGuid;

            switch (command.ConnectionMode)
            {
                case MakeConnectionCommand.Mode.Begin:
                    BeginConnection(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.End:
                    EndConnection(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.BeginShiftReconnections:
                    BeginShiftReconnections(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.EndShiftReconnections:
                    EndShiftReconnections(nodeId, command.PortIndex, command.Type);
                    break;

                // TODO - can be removed in a future version of Dynamo - DYN-1729
                case MakeConnectionCommand.Mode.EndAndStartCtrlConnection:
                    BeginCreateConnections(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.BeginDuplicateConnection:
                    BeginDuplicateConnection(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.BeginCreateConnections:
                    BeginCreateConnections(nodeId, command.PortIndex, command.Type);
                    break;

                case MakeConnectionCommand.Mode.Cancel:
                    CancelConnections();
                    break;
            }
        }

        private void BeginConnection(Guid nodeId, int portIndex, PortType portType)
        {
            bool isInPort = portType == PortType.Input;
            activeStartPorts = null;

            if (CurrentWorkspace.GetModelInternal(nodeId) is not NodeModel node)
                return;
            try
            {
                PortModel portModel = isInPort ? node.InPorts[portIndex] : node.OutPorts[portIndex];

                // Test if port already has a connection, if so grab it and begin connecting 
                // to somewhere else (we don't allow the grabbing of the start connector).
                if (portModel.Connectors.Count > 0 && portModel.Connectors[0].Start != portModel)
                {
                    activeStartPorts = new PortModel[] { portModel.Connectors[0].Start };
                    firstStartPort = portModel.Connectors[0].Start;
                    // Disconnect the connector model from its start and end ports
                    // and remove it from the connectors collection. This will also
                    // remove the view model.
                    ConnectorModel connector = portModel.Connectors[0];
                    if (CurrentWorkspace.Connectors.Contains(connector))
                    {
                        var models = new List<ModelBase> { connector };
                        CurrentWorkspace.SaveAndDeleteModels(models);
                        connector.Delete();
                    }
                }
                else
                {
                    activeStartPorts = new PortModel[] { portModel };
                    firstStartPort = isInPort ? null : portModel; // Only assign firstStartPort if the port selected is an output port
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to begin connection." + "\n" + ex.Message);
            }
        }

        private void BeginDuplicateConnection(Guid nodeId, int portIndex, PortType portType)
        {
            // If the port clicked is an output port, begin connection as per normal
            if (portType == PortType.Output)
            {
                BeginConnection(nodeId, portIndex, portType);
                return;
            }

            // Otherwise, if the port is an input port, check if the port already has a connection.
            // If it does, duplicate the existing connection.
            if (!(CurrentWorkspace.GetModelInternal(nodeId) is NodeModel node))
            {
                return;
            }

            var portModel = node.InPorts[portIndex];
            if (portModel.Connectors.Count == 0)
            {
                // If the port doesn't have any existing connections, begin connection as per normal
                BeginConnection(nodeId, portIndex, portType);
                return;
            }
            // Duplicate the existing connection
            activeStartPorts = new PortModel[] { portModel.Connectors[0].Start };
            firstStartPort = portModel.Connectors[0].Start;
        }

        private void BeginShiftReconnections(Guid nodeId, int portIndex, PortType portType)
        {
            if (portType == PortType.Input) return; //only handle multiple connections when the port selected is an output port
            if (!(CurrentWorkspace.GetModelInternal(nodeId) is NodeModel node)) return;

            PortModel selectedPort = node.OutPorts[portIndex];

            var selectedConnectors = new List<ConnectorModel>();
            selectedConnectors = selectedPort.Connectors.Where(x => x.End.Owner.IsSelected).ToList();

            // If no connectors are selected, process all of the associated nodes
            if (selectedConnectors.Count() <= 0)
            {
                selectedConnectors = selectedPort.Connectors.ToList();
            }

            int numOfConnectors = selectedConnectors.Count();
            if (numOfConnectors == 0) return;
            
            activeStartPorts = new PortModel[numOfConnectors];

            for (int i = 0; i < numOfConnectors; i++)
            {
                ConnectorModel connector = selectedConnectors[i];
                activeStartPorts[i] = connector.End;
            }
            CurrentWorkspace.SaveAndDeleteModels(selectedConnectors.ToList<ModelBase>());
            return;
        }

        private void EndConnection(Guid nodeId, int portIndex, PortType portType)
        {
            // Check if the node from which the connector starts is valid and has not been deleted
            if (activeStartPorts == null || activeStartPorts.Count() <= 0 || activeStartPorts[0].Owner == null) return;

            var startNode = CurrentWorkspace.GetModelInternal(activeStartPorts[0].Owner.GUID);
            if (startNode == null) return;

            if (!(CurrentWorkspace.GetModelInternal(nodeId) is NodeModel node)) return;

            bool isInPort = portType == PortType.Input;
            
            PortModel portModel = isInPort ? node.InPorts[portIndex] : node.OutPorts[portIndex];

            var models = GetConnectorsToAddAndDelete(portModel, activeStartPorts[0]);

            WorkspaceModel.RecordModelsForUndo(models, CurrentWorkspace.UndoRecorder);
            activeStartPorts = null;
            firstStartPort = null;
        }

        private void EndShiftReconnections(Guid nodeId, int portIndex, PortType portType)
        {
            if (portType == PortType.Input) return; //only handle multiple connections when the port selected is an output port
            if (activeStartPorts == null || activeStartPorts.Count() <= 0) return;

            if (!(CurrentWorkspace.GetModelInternal(nodeId) is NodeModel node)) return;
            PortModel selectedPort = node.OutPorts[portIndex];
            
            var firstModel = GetConnectorsToAddAndDelete(selectedPort, activeStartPorts[0]);
            for (int i = 1; i < activeStartPorts.Count(); i++)
            {
                var models = GetConnectorsToAddAndDelete(selectedPort, activeStartPorts[i]);
                foreach (var m in models)
                {
                    firstModel.Add(m.Key, m.Value);
                }
            }
            WorkspaceModel.RecordModelsForUndo(firstModel, CurrentWorkspace.UndoRecorder);
            activeStartPorts = null;
            return;
        }

        private void BeginCreateConnections(Guid nodeId, int portIndex, PortType portType)
        {
            if (portType == PortType.Output) return; // Only handle ctrl connections if selected port is an input port
            if (firstStartPort == null || activeStartPorts == null || activeStartPorts.Count() <= 0) return;
            if (!(CurrentWorkspace.GetModelInternal(nodeId) is NodeModel node)) return;

            PortModel portModel = node.InPorts[portIndex];
            var models = GetConnectorsToAddAndDelete(portModel, activeStartPorts[0]);
            WorkspaceModel.RecordModelsForUndo(models, CurrentWorkspace.UndoRecorder);
            
            activeStartPorts = new PortModel[] { firstStartPort };
        }

        private void CancelConnections()
        {
            CurrentWorkspace.DeleteSavedModels();
            activeStartPorts = null;
            firstStartPort = null;
            return;
        }

        private static Dictionary<ModelBase, UndoRedoRecorder.UserAction> GetConnectorsToAddAndDelete(
            PortModel endPort, PortModel startPort)
        {
            ConnectorModel connectorToRemove = null;

            // Remove connector if one already exists
            if (endPort.Connectors.Count > 0 && endPort.PortType == PortType.Input)
            {
                connectorToRemove = endPort.Connectors[0];
                connectorToRemove.Delete();
            }

            // We could either connect from an input port to an output port, or 
            // another way around (in which case we swap first and second ports).
            PortModel firstPort, secondPort;
            if (endPort.PortType != PortType.Input)
            {
                firstPort = endPort;
                secondPort = startPort;
            }
            else
            {
                // Create the new connector model
                firstPort = startPort;
                secondPort = endPort;
            }

            ConnectorModel newConnectorModel = ConnectorModel.Make(
                firstPort.Owner,
                secondPort.Owner,
                firstPort.Index,
                secondPort.Index);

            // Record the creation of connector in the undo recorder.
            var models = new Dictionary<ModelBase, UndoRedoRecorder.UserAction>();
            if (connectorToRemove != null)
            {
                models.Add(connectorToRemove, UndoRedoRecorder.UserAction.Deletion);
            }
            if (newConnectorModel != null)
            {
                models.Add(newConnectorModel, UndoRedoRecorder.UserAction.Creation);
            }
            return models;
        }

        private void DeleteModelImpl(DeleteModelCommand command)
        {
            RequestHideNodeAutoCompleteBar?.Invoke();

            var modelsToDelete = new List<ModelBase>();
            if (command.ModelGuid == Guid.Empty)
            {
                // When nothing is specified then it means all selected models.
                modelsToDelete.AddRange(DynamoSelection.Instance.Selection.OfType<ModelBase>());
            }
            else
            {
                modelsToDelete.AddRange(command.ModelGuids.Select(guid => CurrentWorkspace.GetModelInternal(guid)));
            }

            if (!command.CanDeleteTransientNodes)
            {
                // Remove transient nodes from the list of models to delete.
                modelsToDelete.RemoveAll(model => (model as NodeModel)?.IsTransient == true);
            }

            DeleteModelInternal(modelsToDelete);
        }

        private void UngroupModelImpl(UngroupModelCommand command)
        {
            if (command.ModelGuid == Guid.Empty)
                return;

            var modelsToUngroup = command.ModelGuids.Select(guid => CurrentWorkspace.GetModelInternal(guid)).ToList();

            UngroupModel(modelsToUngroup);
        }

        private void AddToGroupImpl(AddModelToGroupCommand command)
        {
            if (command.ModelGuid == Guid.Empty)
                return;

            var modelsToGroup = command.ModelGuids.Select(guid => CurrentWorkspace.GetModelInternal(guid)).ToList();
            if (modelsToGroup.OfType<NodeModel>().Any())
            {
                var nodeModels = modelsToGroup.OfType<NodeModel>();
                var pinnedNotes = CurrentWorkspace.Notes
                    .Where(x => x.PinnedNode != null && nodeModels.Contains(x.PinnedNode));

                if (pinnedNotes.Any())
                {
                    modelsToGroup.AddRange(pinnedNotes);
                }
            }

            AddToGroup(modelsToGroup);
        }

        private void AddGroupsToGroupImpl(AddGroupToGroupCommand command)
        {
            if (command.ModelGuid == Guid.Empty) return;

            // Getting all the annotation models from Guids
            var modelsToGroup = command.ModelGuids
                .Select(guid => CurrentWorkspace.GetModelInternal(guid))
                .ToList();

            AddGroupsToGroup(modelsToGroup, command.HostGroupGuid);
        }

        private void UndoRedoImpl(UndoRedoCommand command)
        {
            switch (command.CmdOperation)
            {
                case UndoRedoCommand.Operation.Undo:
                    CurrentWorkspace.Undo();
                    break;
                case UndoRedoCommand.Operation.Redo:
                    CurrentWorkspace.Redo();
                    break;
            }
        }

        private void SendModelEventImpl(ModelEventCommand command)
        {
            foreach (var guid in command.ModelGuids)
            {
                CurrentWorkspace.SendModelEvent(guid, command.EventName, command.Value);
            }
        }

        private void UpdateModelValueImpl(UpdateModelValueCommand command)
        {
            WorkspaceModel targetWorkspace = CurrentWorkspace;
            if (!command.WorkspaceGuid.Equals(Guid.Empty))
                targetWorkspace = Workspaces.FirstOrDefault(w => w.Guid.Equals(command.WorkspaceGuid));
            try
            {
                targetWorkspace?.UpdateModelValue(command.ModelGuids,
                    command.Name, command.Value);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex.Message);
            }
        }

        private void ConvertNodesToCodeImpl(ConvertNodesToCodeCommand command)
        {
            var libServices = new LibraryCustomizationServices(pathManager);
            var namingProvider = new NamingProvider(EngineController.LibraryServices.LibraryManagementCore, libServices);
            CurrentWorkspace.ConvertNodesToCodeInternal(EngineController, namingProvider);

            CurrentWorkspace.HasUnsavedChanges = true;
        }

        private void CreateCustomNodeImpl(CreateCustomNodeCommand command)
        {
            var workspace = CustomNodeManager.CreateCustomNode(
                command.Name,
                command.Category,
                command.Description, 
                command.ModelGuid);

            AddWorkspace(workspace);
            CurrentWorkspace = workspace;
        }

        private void SwitchWorkspaceImpl(SwitchTabCommand command)
        {
            // We don't attempt to null-check here, we need it to fail fast.
            CurrentWorkspace = Workspaces.ElementAt(command.WorkspaceModelIndex);
        }

        private void CreatePresetStateImpl(AddPresetCommand command)
        {
            var preset = this.CurrentWorkspace.AddPreset(command.PresetStateName,command.PresetStateDescription,command.ModelGuids);

            CurrentWorkspace.RecordCreatedModel(preset);
        }
        private void SetWorkSpaceToStateImpl(ApplyPresetCommand command)
        {
            var workspaceToSet = this.Workspaces.Where(x => x.Guid == command.WorkSpaceID).First();
            if (workspaceToSet == null)
            {
                return;
            }
            var state = workspaceToSet.Presets.Where(x => x.GUID == command.StateID).FirstOrDefault();

            workspaceToSet.ApplyPreset (state);
        }

    }
}
