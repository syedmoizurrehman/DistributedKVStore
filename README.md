# Distributed Key-Value Data Store
A distributed Key-Value data store based on Apache's Cassandra using C#.

## Project Structure
The project consists of Application layer, Network layer, Data Access layer and an interface layer.

### Application Layer
Deals with all the system design principles used for sending and receiving messages. Main content is included in files ApplicationLayer.cs and Message.cs.

### Network Layer
This module deals with all the socket operations required for the project. It supports asynchronous socket operations for sending and receiving data. For data transfer, TCP is used due to required reliability when writing or reading data from the database.

### Data Access
The underlying database used is SQLite since actual data storage is not focus of this project. The database has one table with three columns, two for key and value, and one for storing time stamps. The Coordinator node also stores a look-up table to quickly check whether a key has been written to database before attempting to read it.

### Interface
Nothing fancy, just regular CLI allowing the user to view the status of various operations as they are executed, and command the system.

## Sharding and Replication
For each key, a hash value is generated which determines the storage nodes. This includes sharding nodes as well as replication nodes.

## Consistency
To keep the data consistent, the coordinator uses time stamps of each key-value pair. These time stamps are used for reading the most recently updated data. The coordinator compares the time stamps of all replicas and selects the node with latest time stamp.
Each message exchange contains the network information of both sender and receiver with time stamps indicating when was the information about a particular node in network updated. This is used to ensure that each node will have the most updated information regarding the whole network. 

## Stabilization
The ring size is stored in coordinator look-up for every key. Any time a read/delete is requested for a key, the coordinator checks the stored ring size with current ring size. In case a difference is found, it invokes the stabilization algorithm which performs the following steps:
1. Query all nodes with stored ring size to see ensure that quorum nodes are up.
2. Delete the record from all the nodes mapped to old ring size.
3. Write the key as a new key.
4. Update ring size to current ring size corresponding to the key in coordinator look-up.

## Membership Protocol
A prototype version of Gossip Protocol for dissemination and failure dectection is used. This feature is mostly under development.
