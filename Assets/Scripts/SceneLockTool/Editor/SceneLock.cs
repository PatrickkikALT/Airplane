namespace Utils.Core.SceneLockTool
{
    /// <summary>
    /// Represents a scene object associated with a scene lock, providing details about the lock status and project information.
    /// </summary>
    [System.Serializable]
    public class SceneLockSceneObject
    {
        public SceneLock SceneLock = null;
        public string projectID = "";
        public string projectName = "";
        public bool HasSceneLock = false;
        public bool HasSceneLockActive => SceneLock != null;
    }

    /// <summary>
    /// Represents a lock on a scene in a project, providing details about the lock's attributes and ownership.
    /// </summary>
    [System.Serializable]
    public class SceneLock
    {
        public string SceneLockID { get; private set; }
        public string ProjectID { get; private set; }
        public string SceneID { get; private set; }
        public string OwnerID { get; private set; }
        public string OwnerName { get; private set; }
        public string OwnerDeviceID { get; private set; }
        public string LockTime { get; private set; }

        public SceneLock(string sceneLockID, string projectID, string sceneID, string ownerID, string ownerName, string ownerDeviceID, string lockTime)
        {
            this.SceneLockID = sceneLockID;
            this.ProjectID = projectID;
            this.SceneID = sceneID;
            this.OwnerID = ownerID;
            this.OwnerName = ownerName;
            this.LockTime = lockTime;
            this.OwnerDeviceID = ownerDeviceID;
        }
    }
}